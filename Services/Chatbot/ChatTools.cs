using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using SkillLink_dotnet.Data;
using SkillLink_dotnet.Models;
using AlgoliaService = SkillLink.Services.AlgoliaService;

namespace SkillLink_dotnet.Services.Chatbot;

public sealed class BuscarPublicacionesTool(
    ApplicationDbContext db,
    AlgoliaService algolia,
    ChatCardFactory cardFactory,
    ILogger<BuscarPublicacionesTool> logger) : IChatTool
{
    public string Name => "buscar_publicaciones";

    public string Description =>
        "Busca publicaciones activas de SkillLink (ofertas, proyectos, experiencias, búsquedas). " +
        "Úsala cuando el usuario pida encontrar trabajo, ofertas, proyectos o servicios publicados. " +
        "Devuelve tarjetas públicas, nunca teléfonos ni datos privados.";

    public JsonObject Parameters { get; } = new()
    {
        ["type"] = "OBJECT",
        ["properties"] = new JsonObject
        {
            ["query"] = new JsonObject
            {
                ["type"] = "STRING",
                ["description"] = "Texto de búsqueda: oficio, habilidad, problema o palabra clave."
            },
            ["categoria"] = new JsonObject
            {
                ["type"] = "STRING",
                ["description"] = "Opcional: construccion, tecnologia, diseno, hogar u otros."
            },
            ["ciudad"] = new JsonObject
            {
                ["type"] = "STRING",
                ["description"] = "Opcional: ciudad o departamento del Perú."
            },
            ["modalidad"] = new JsonObject
            {
                ["type"] = "STRING",
                ["description"] = "Opcional: tiempo_completo, medio_tiempo o por_proyecto."
            },
            ["tipo"] = new JsonObject
            {
                ["type"] = "STRING",
                ["description"] = "Opcional: oferta, convocatoria, experiencia, busqueda, proyecto o empresa_info."
            },
            ["limite"] = new JsonObject
            {
                ["type"] = "INTEGER",
                ["description"] = "Máximo de tarjetas a devolver, entre 1 y 5."
            }
        },
        ["required"] = new JsonArray { JsonValue.Create("query") }
    };

    public async Task<JsonNode> ExecuteAsync(ChatToolContext context, JsonNode? args, CancellationToken ct)
    {
        var query = ChatToolJson.Str(args, "query")?.Trim();
        if (string.IsNullOrWhiteSpace(query))
            return ChatToolJson.Error("Necesito una palabra clave o descripción para buscar publicaciones.");

        var categoria = ChatToolJson.Str(args, "categoria")?.Trim();
        var ciudad = ChatToolJson.Str(args, "ciudad")?.Trim();
        var modalidad = ChatToolJson.Str(args, "modalidad")?.Trim();
        var tipo = ChatToolJson.Str(args, "tipo")?.Trim();
        var limite = Math.Clamp(ChatToolJson.Int(args, "limite", 3), 1, 5);

        var idsAlgolia = algolia.IsConfigured
            ? await algolia.SearchPublicacionIdsAsync(query, 30)
            : null;

        if (idsAlgolia is { Count: 0 })
            return ChatToolJson.Ok("No encontré publicaciones activas que coincidan con esa búsqueda.");

        var q = db.Publicaciones.AsNoTracking()
            .Include(p => p.Autor).ThenInclude(a => a!.Perfil)
            .Include(p => p.Autor).ThenInclude(a => a!.Empresa)
            .Where(p => p.Activa);

        if (idsAlgolia is { Count: > 0 })
            q = q.Where(p => idsAlgolia.Contains(p.Id));
        else
            q = q.Where(p =>
                (p.Titulo != null && p.Titulo.Contains(query)) ||
                (p.HabilidadesReq != null && p.HabilidadesReq.Contains(query)) ||
                p.Contenido.Contains(query));

        if (!string.IsNullOrWhiteSpace(categoria))
            q = q.Where(p => p.Categoria == categoria);
        if (!string.IsNullOrWhiteSpace(ciudad))
            q = q.Where(p => p.Autor!.Perfil!.Ciudad == ciudad || p.Autor!.Empresa!.Ciudad == ciudad);
        if (!string.IsNullOrWhiteSpace(modalidad))
            q = q.Where(p => p.Modalidad == modalidad);
        if (!string.IsNullOrWhiteSpace(tipo))
            q = q.Where(p => p.Tipo == tipo);

        var pubs = await q.ToListAsync(ct);
        if (idsAlgolia is { Count: > 0 })
        {
            var orden = idsAlgolia.Select((id, indice) => (id, indice))
                .ToDictionary(x => x.id, x => x.indice);
            pubs = pubs.OrderBy(p => orden.GetValueOrDefault(p.Id, int.MaxValue)).Take(limite).ToList();
        }
        else
        {
            pubs = pubs.OrderByDescending(p => p.FechaCreacion).Take(limite).ToList();
        }

        var ratings = await CargarRatingsAsync(pubs.Select(p => p.Id).ToList(), ct);
        var cards = new JsonArray();
        foreach (var p in pubs)
        {
            var rating = ratings.TryGetValue(p.Id, out var r) ? r.Promedio : (double?)null;
            var total = ratings.TryGetValue(p.Id, out var r2) ? r2.Total : 0;
            var motivo = $"Coincide con «{query}»";
            if (!string.IsNullOrWhiteSpace(p.Categoria)) motivo += $" · {Catalogos.Categoria(p.Categoria)}";
            if (!string.IsNullOrWhiteSpace(Catalogos.Ciudad(p)) && Catalogos.Ciudad(p) != "—")
                motivo += $" · {Catalogos.Ciudad(p)}";
            cards.Add(cardFactory.Publicacion(p, rating, total, motivo));
        }

        logger.LogInformation("Chatbot buscó publicaciones: {Query} -> {Total} resultados.", query, cards.Count);
        var resumen = cards.Count == 0
            ? "No encontré publicaciones activas con esos filtros."
            : $"Encontré {cards.Count} publicación(es) que coinciden con tu búsqueda.";
        return ChatToolJson.Ok(resumen, cards);
    }

    private async Task<Dictionary<int, (double Promedio, int Total)>> CargarRatingsAsync(
        List<int> publicacionIds, CancellationToken ct)
    {
        if (publicacionIds.Count == 0) return [];
        return await db.Valoraciones.AsNoTracking()
            .Where(v => v.PublicacionId != null && publicacionIds.Contains(v.PublicacionId.Value)
                        && v.Activo && v.Calificacion != null)
            .GroupBy(v => v.PublicacionId!.Value)
            .Select(g => new
            {
                PublicacionId = g.Key,
                Total = g.Count(),
                Promedio = g.Average(v => v.Calificacion!.Value)
            })
            .ToDictionaryAsync(x => x.PublicacionId, x => (Promedio: x.Promedio, Total: x.Total), ct);
    }
}

public sealed class BuscarTecnicosTool(
    ApplicationDbContext db,
    ChatCardFactory cardFactory,
    ILogger<BuscarTecnicosTool> logger) : IChatTool
{
    public string Name => "buscar_tecnicos";

    public string Description =>
        "Busca técnicos y trabajadores con cuenta aprobada por especialidad, habilidades y ciudad. " +
        "Devuelve solo información pública: nombre, especialidad, ciudad, disponibilidad, habilidades y calificación. " +
        "Nunca devuelve teléfono, DNI, correo ni CV.";

    public JsonObject Parameters { get; } = new()
    {
        ["type"] = "OBJECT",
        ["properties"] = new JsonObject
        {
            ["especialidad"] = new JsonObject
            {
                ["type"] = "STRING",
                ["description"] = "Oficio o especialidad buscada: gasfitería, electricidad, soldadura, diseño, etc."
            },
            ["ciudad"] = new JsonObject
            {
                ["type"] = "STRING",
                ["description"] = "Opcional: ciudad o departamento."
            },
            ["limite"] = new JsonObject
            {
                ["type"] = "INTEGER",
                ["description"] = "Máximo de tarjetas a devolver, entre 1 y 5."
            }
        }
    };

    public async Task<JsonNode> ExecuteAsync(ChatToolContext context, JsonNode? args, CancellationToken ct)
    {
        var especialidad = ChatToolJson.Str(args, "especialidad")?.Trim();
        var ciudad = ChatToolJson.Str(args, "ciudad")?.Trim();
        if (string.IsNullOrWhiteSpace(especialidad) && string.IsNullOrWhiteSpace(ciudad))
            return ChatToolJson.Error("Necesito saber qué tipo de técnico buscas o en qué ciudad. Por ejemplo: gasfitero en Arequipa.");

        var limite = Math.Clamp(ChatToolJson.Int(args, "limite", 3), 1, 5);
        var q = db.Perfiles.AsNoTracking()
            .Include(p => p.Usuario)
            .Where(p => p.Usuario != null
                        && p.Usuario.Rol == Roles.Trabajador
                        && p.Usuario.EstadoCuenta == EstadosCuenta.Aprobado);

        if (!string.IsNullOrWhiteSpace(especialidad))
            q = q.Where(p =>
                (p.Especialidad != null && p.Especialidad.Contains(especialidad)) ||
                (p.Habilidades != null && p.Habilidades.Contains(especialidad)) ||
                (p.Descripcion != null && p.Descripcion.Contains(especialidad)));
        if (!string.IsNullOrWhiteSpace(ciudad))
            q = q.Where(p => p.Ciudad == ciudad);

        var perfiles = await q
            .OrderBy(p => p.Disponibilidad == "disponible" ? 0 : 1)
            .ThenBy(p => p.NombreCompleto)
            .Take(limite)
            .ToListAsync(ct);

        var ratings = await CargarRatingsAsync(perfiles.Select(p => p.UsuarioId).ToList(), ct);
        var cards = new JsonArray();
        foreach (var p in perfiles)
        {
            if (p.Usuario is null) continue;
            var rating = ratings.TryGetValue(p.UsuarioId, out var r) ? r.Promedio : (double?)null;
            var total = ratings.TryGetValue(p.UsuarioId, out var r2) ? r2.Total : 0;
            var motivo = string.IsNullOrWhiteSpace(especialidad)
                ? $"Disponible en {p.Ciudad ?? "su zona"}"
                : $"Coincide con «{especialidad}»";
            if (!string.IsNullOrWhiteSpace(p.Disponibilidad)) motivo += $" · {p.Disponibilidad}";
            cards.Add(cardFactory.Tecnico(p, p.Usuario, rating, total, motivo));
        }

        logger.LogInformation("Chatbot buscó técnicos: {Especialidad} / {Ciudad} -> {Total} resultados.",
            especialidad, ciudad, cards.Count);
        var resumen = cards.Count == 0
            ? "No encontré técnicos aprobados con esos criterios. Prueba con otra especialidad o ciudad."
            : $"Encontré {cards.Count} técnico(s) que podrían ayudarte.";
        return ChatToolJson.Ok(resumen, cards);
    }

    private async Task<Dictionary<string, (double Promedio, int Total)>> CargarRatingsAsync(
        List<string> usuarios, CancellationToken ct)
    {
        if (usuarios.Count == 0) return [];
        return await db.Valoraciones.AsNoTracking()
            .Where(v => usuarios.Contains(v.DestinatarioId) && v.Activo && v.Calificacion != null)
            .GroupBy(v => v.DestinatarioId)
            .Select(g => new
            {
                UsuarioId = g.Key,
                Total = g.Count(),
                Promedio = g.Average(v => v.Calificacion!.Value)
            })
            .ToDictionaryAsync(x => x.UsuarioId, x => (Promedio: x.Promedio, Total: x.Total), ct);
    }
}

public sealed class ObtenerPublicacionTool(
    ApplicationDbContext db,
    ChatCardFactory cards) : IChatTool
{
    public string Name => "obtener_publicacion";

    public string Description =>
        "Obtiene el detalle público de una publicación activa por su id. " +
        "Úsala si el usuario pide más información de una publicación o necesitas comparar detalles.";

    public JsonObject Parameters { get; } = new()
    {
        ["type"] = "OBJECT",
        ["properties"] = new JsonObject
        {
            ["id"] = new JsonObject
            {
                ["type"] = "INTEGER",
                ["description"] = "Id de la publicación."
            }
        },
        ["required"] = new JsonArray { JsonValue.Create("id") }
    };

    public async Task<JsonNode> ExecuteAsync(ChatToolContext context, JsonNode? args, CancellationToken ct)
    {
        var id = ChatToolJson.Int(args, "id", 0);
        if (id <= 0) return ChatToolJson.Error("Necesito el id de una publicación válida.");

        var p = await db.Publicaciones.AsNoTracking()
            .Include(x => x.Autor).ThenInclude(a => a!.Perfil)
            .Include(x => x.Autor).ThenInclude(a => a!.Empresa)
            .FirstOrDefaultAsync(x => x.Id == id && x.Activa, ct);
        if (p is null) return ChatToolJson.Ok("No encontré esa publicación o ya no está activa.");

        var rating = await db.Valoraciones.AsNoTracking()
            .Where(v => v.PublicacionId == id && v.Activo && v.Calificacion != null)
            .GroupBy(v => v.PublicacionId)
            .Select(g => new { Total = g.Count(), Promedio = g.Average(v => v.Calificacion!.Value) })
            .FirstOrDefaultAsync(ct);

        var card = cards.Publicacion(p, rating?.Promedio, rating?.Total ?? 0, "Detalle de la publicación");
        var item = new JsonObject
        {
            ["id"] = p.Id,
            ["titulo"] = ChatPrivacy.Sanitize(p.Titulo),
            ["contenido"] = ChatPrivacy.Sanitize(p.Contenido),
            ["habilidades"] = ChatPrivacy.Sanitize(p.HabilidadesReq),
            ["categoria"] = Catalogos.Categoria(p.Categoria),
            ["modalidad"] = Catalogos.Modalidad(p.Modalidad),
            ["precio"] = ChatPrivacy.Sanitize(Catalogos.Precio(p)),
            ["ciudad"] = ChatPrivacy.Sanitize(Catalogos.Ciudad(p)),
            ["autor"] = ChatPrivacy.Sanitize(ChatCardFactory.NombrePublicoSeguro(p.Autor)),
        };
        return ChatToolJson.Ok("Detalle de la publicación.", new JsonArray { card }, new JsonArray { item });
    }
}

public sealed class CompararPublicacionesTool(
    ApplicationDbContext db,
    ChatCardFactory cards) : IChatTool
{
    public string Name => "comparar_publicaciones";

    public string Description =>
        "Compara entre 2 y 3 publicaciones activas por id. Devuelve precio, ciudad, modalidad, calificación y datos públicos " +
        "para que puedas explicar cuál conviene según lo que pidió el usuario.";

    public JsonObject Parameters { get; } = new()
    {
        ["type"] = "OBJECT",
        ["properties"] = new JsonObject
        {
            ["ids"] = new JsonObject
            {
                ["type"] = "ARRAY",
                ["items"] = new JsonObject { ["type"] = "INTEGER" },
                ["description"] = "Ids de 2 a 3 publicaciones."
            }
        },
        ["required"] = new JsonArray { JsonValue.Create("ids") }
    };

    public async Task<JsonNode> ExecuteAsync(ChatToolContext context, JsonNode? args, CancellationToken ct)
    {
        var ids = ChatToolJson.IntList(args, "ids");
        if (ids.Count < 2)
            return ChatToolJson.Error("Necesito al menos dos publicaciones para comparar.");

        var pubs = await db.Publicaciones.AsNoTracking()
            .Include(p => p.Autor).ThenInclude(a => a!.Perfil)
            .Include(p => p.Autor).ThenInclude(a => a!.Empresa)
            .Where(p => p.Activa && ids.Contains(p.Id))
            .ToListAsync(ct);

        var orden = ids.Select((id, indice) => (id, indice)).ToDictionary(x => x.id, x => x.indice);
        pubs = pubs.OrderBy(p => orden.GetValueOrDefault(p.Id, int.MaxValue)).ToList();

        var ratings = await db.Valoraciones.AsNoTracking()
            .Where(v => v.PublicacionId != null && ids.Contains(v.PublicacionId.Value)
                        && v.Activo && v.Calificacion != null)
            .GroupBy(v => v.PublicacionId!.Value)
            .Select(g => new
            {
                PublicacionId = g.Key,
                Total = g.Count(),
                Promedio = g.Average(v => v.Calificacion!.Value)
            })
            .ToDictionaryAsync(x => x.PublicacionId, x => (Promedio: x.Promedio, Total: x.Total), ct);

        var tarjetas = new JsonArray();
        foreach (var p in pubs)
        {
            var rating = ratings.TryGetValue(p.Id, out var r) ? r.Promedio : (double?)null;
            var total = ratings.TryGetValue(p.Id, out var r2) ? r2.Total : 0;
            tarjetas.Add(cards.Publicacion(p, rating, total, "Comparación"));
        }

        if (tarjetas.Count == 0)
            return ChatToolJson.Ok("No encontré publicaciones activas con esos ids.");

        return ChatToolJson.Ok($"Comparando {tarjetas.Count} publicaciones.", tarjetas);
    }
}

public sealed class EstadoSolicitudesTool(
    ApplicationDbContext db,
    ILogger<EstadoSolicitudesTool> logger) : IChatTool
{
    public string Name => "estado_solicitudes";

    public string Description =>
        "Consulta las postulaciones del usuario autenticado. Si es trabajador, devuelve sus postulaciones enviadas; " +
        "si es empresa, devuelve las postulaciones recibidas en sus publicaciones. Nunca mezcla datos de otros usuarios.";

    public JsonObject Parameters { get; } = new()
    {
        ["type"] = "OBJECT",
        ["properties"] = new JsonObject()
    };

    public async Task<JsonNode> ExecuteAsync(ChatToolContext context, JsonNode? args, CancellationToken ct)
    {
        if (context.Role is not (Roles.Trabajador or Roles.Empresa))
            return ChatToolJson.Error("El estado de postulaciones solo está disponible para cuentas de trabajador o empresa.");

        var baseQuery = db.Solicitudes.AsNoTracking().Where(s => s.Activa);
        if (context.Role == Roles.Trabajador)
            baseQuery = baseQuery.Where(s => s.TrabajadorId == context.UserId);
        else
            baseQuery = baseQuery.Where(s => s.Publicacion!.AutorId == context.UserId);

        var resumenEstados = await baseQuery
            .GroupBy(s => s.Estado)
            .Select(g => new { Estado = g.Key, Total = g.Count() })
            .ToListAsync(ct);
        var total = resumenEstados.Sum(x => x.Total);
        var pendientes = resumenEstados.FirstOrDefault(x => x.Estado == EstadosSolicitud.Pendiente)?.Total ?? 0;
        var aceptadas = resumenEstados.FirstOrDefault(x => x.Estado == EstadosSolicitud.Aceptada)?.Total ?? 0;
        var rechazadas = resumenEstados.FirstOrDefault(x => x.Estado == EstadosSolicitud.Rechazada)?.Total ?? 0;

        var lista = await baseQuery
            .Include(s => s.Publicacion).ThenInclude(p => p!.Autor).ThenInclude(a => a!.Empresa)
            .Include(s => s.Trabajador).ThenInclude(t => t!.Perfil)
            .OrderByDescending(s => s.FechaPostulacion)
            .Take(10)
            .ToListAsync(ct);

        var items = new JsonArray();
        foreach (var s in lista)
        {
            var contraparte = context.Role == Roles.Trabajador
                ? s.Publicacion?.Autor?.Empresa?.RazonSocial ?? "Empresa"
                : s.Trabajador?.Perfil?.NombreCompleto ?? "Trabajador";
            items.Add(new JsonObject
            {
                ["estado"] = s.Estado,
                ["titulo"] = ChatPrivacy.Sanitize(s.Publicacion?.Titulo),
                ["contraparte"] = ChatPrivacy.Sanitize(contraparte),
                ["fecha"] = s.FechaPostulacion,
            });
        }

        logger.LogInformation("Chatbot consultó {Total} solicitudes para {UserId}.", total, context.UserId);

        var resumen = total == 0
            ? "No tienes postulaciones activas registradas."
            : $"Tienes {total} postulación(es) activa(s): {pendientes} pendiente(s), {aceptadas} aceptada(s) y {rechazadas} rechazada(s).";
        return ChatToolJson.Ok(resumen, items: items);
    }
}

public sealed class EstadoNotificacionesTool(
    ApplicationDbContext db) : IChatTool
{
    public string Name => "estado_notificaciones";

    public string Description =>
        "Devuelve las notificaciones recientes del usuario autenticado (solo las propias). " +
        "Úsala cuando pregunte si tiene respuestas, avisos o novedades.";

    public JsonObject Parameters { get; } = new()
    {
        ["type"] = "OBJECT",
        ["properties"] = new JsonObject()
    };

    public async Task<JsonNode> ExecuteAsync(ChatToolContext context, JsonNode? args, CancellationToken ct)
    {
        var lista = await db.Notificaciones.AsNoTracking()
            .Where(n => n.UsuarioId == context.UserId)
            .OrderByDescending(n => n.Fecha)
            .Take(10)
            .ToListAsync(ct);

        var items = new JsonArray();
        foreach (var n in lista)
        {
            items.Add(new JsonObject
            {
                ["titulo"] = ChatPrivacy.Sanitize(n.Titulo),
                ["mensaje"] = ChatPrivacy.Sanitize(n.Mensaje),
                ["enlace"] = ChatPrivacy.Sanitize(n.Enlace),
                ["leida"] = n.Leida,
                ["fecha"] = n.Fecha,
            });
        }

        var sinLeer = lista.Count(n => !n.Leida);
        var resumen = lista.Count == 0
            ? "No tienes notificaciones recientes."
            : $"Tienes {lista.Count} notificación(es) reciente(s), {sinLeer} sin leer.";
        return ChatToolJson.Ok(resumen, items: items);
    }
}

public sealed class FaqPlataformaTool(ChatbotFaqService faq) : IChatTool
{
    public string Name => "faq_plataforma";

    public string Description =>
        "Resuelve dudas sobre cómo funciona SkillLink: publicar, postular, contactar, cuenta, pagos, seguridad, modalidades y soporte. " +
        "Devuelve respuestas curadas; no inventes información que no esté en el resultado.";

    public JsonObject Parameters { get; } = new()
    {
        ["type"] = "OBJECT",
        ["properties"] = new JsonObject
        {
            ["pregunta"] = new JsonObject
            {
                ["type"] = "STRING",
                ["description"] = "Pregunta o tema sobre el funcionamiento de la plataforma."
            }
        },
        ["required"] = new JsonArray { JsonValue.Create("pregunta") }
    };

    public Task<JsonNode> ExecuteAsync(ChatToolContext context, JsonNode? args, CancellationToken ct)
    {
        var pregunta = ChatToolJson.Str(args, "pregunta")?.Trim() ?? string.Empty;
        var resultados = faq.Buscar(pregunta, 3);

        if (resultados.Count == 0)
        {
            var temas = new JsonArray();
            foreach (var entrada in faq.Todas().Take(8))
                temas.Add(new JsonObject
                {
                    ["id"] = entrada.Id,
                    ["tema"] = entrada.Preguntas.FirstOrDefault() ?? entrada.Id,
                });
            return Task.FromResult<JsonNode>(ChatToolJson.Ok(
                "No encontré una respuesta exacta en las preguntas frecuentes. Ofrece estos temas o pide más detalle.",
                items: temas));
        }

        var items = new JsonArray();
        foreach (var entrada in resultados)
        {
            items.Add(new JsonObject
            {
                ["id"] = entrada.Id,
                ["pregunta"] = entrada.Preguntas.FirstOrDefault(),
                ["respuesta"] = entrada.Respuesta,
            });
        }

        return Task.FromResult<JsonNode>(ChatToolJson.Ok(
            $"Encontré {resultados.Count} respuesta(s) en las preguntas frecuentes.",
            items: items));
    }
}
