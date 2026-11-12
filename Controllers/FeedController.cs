using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SkillLink_dotnet.Data;
using SkillLink_dotnet.Filters;
using SkillLink_dotnet.Models;
using SkillLink_dotnet.Services;
using SkillLink_dotnet.Services.Geo;
using SkillLink_dotnet.Services.Ml;
using SkillLink_dotnet.ViewModels;

namespace SkillLink_dotnet.Controllers;

/// <summary>Feed laboral: listar / nueva / detalle / comentar (feed.py).</summary>
public class FeedController(ApplicationDbContext db, UserManager<ApplicationUser> users, ImageUploadService uploads, SkillLink.Services.AlgoliaService algolia, ReputacionService reputacion, ModeracionService moderacion) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? seccion = "laboral", string? tipo = null, string? q = null, string? ciudad = null, string? modalidad = null, string? categoria = null, int? estrellas = null, string? opCalif = ">=", int pagina = 1)
    {
        var ciudadQuery = Catalogos.NormalizarDepartamento(ciudad);
        ViewBag.CiudadExplicita = ciudadQuery is not null;

        // Solo se aceptan departamentos del catálogo. Si la ciudad explícita no
        // coincide, se muestran todas. Sin filtro explícito se usa la cookie,
        // también validada, como respaldo (nunca coordenadas).
        if (!string.IsNullOrWhiteSpace(ciudad))
            ciudad = ciudadQuery;
        else
            ciudad = GeoCookies.EsTodas(Request)
                ? null
                : Catalogos.NormalizarDepartamento(GeoCookies.LeerCiudad(Request));

        var query = db.Publicaciones
            .Include(p => p.Autor).ThenInclude(a => a!.Perfil)
            .Include(p => p.Autor).ThenInclude(a => a!.Empresa)
            .Where(p => p.Activa).AsQueryable();
        List<int>? idsAlgolia = null;
        if (!string.IsNullOrWhiteSpace(q))
        {
            // La búsqueda del feed usa Algolia como único motor; sin fallback LIKE a BD.
            idsAlgolia = await algolia.SearchPublicacionIdsAsync(q, 50);
            if (idsAlgolia is null || idsAlgolia.Count == 0)
            {
                ViewBag.UsaAlgolia = idsAlgolia is not null;
                ViewBag.SearchError = idsAlgolia is null
                    ? "La búsqueda no está disponible en este momento. Intenta nuevamente."
                    : null;
                ViewBag.CommentsCount = new Dictionary<int, int>();
                ViewBag.Ratings = new Dictionary<int, (double Promedio, int Total)>();
                ViewBag.Seccion = seccion; ViewBag.Q = q; ViewBag.Ciudad = ciudad; ViewBag.Tipo = tipo;
                ViewBag.Modalidad = modalidad; ViewBag.Categoria = categoria;
                ViewBag.Estrellas = estrellas is >= 1 and <= 5 ? estrellas : null;
                ViewBag.OpCalif = opCalif is "<=" or "=" or ">=" ? opCalif : ">=";
                ViewBag.Pagina = 1; ViewBag.TotalPaginas = 1; ViewBag.Total = 0;
                return View(new List<Publicacion>());
            }
            query = query.Where(p => idsAlgolia.Contains(p.Id));
            ViewBag.UsaAlgolia = true;
        }
        if (!string.IsNullOrWhiteSpace(ciudad))
            query = query.Where(p => p.Autor!.Perfil!.Ciudad == ciudad || p.Autor!.Empresa!.Ciudad == ciudad);
        if (!string.IsNullOrWhiteSpace(tipo))
            query = query.Where(p => p.Tipo == tipo);
        if (!string.IsNullOrWhiteSpace(modalidad))
            query = query.Where(p => p.Modalidad == modalidad);
        if (!string.IsNullOrWhiteSpace(categoria))
            query = query.Where(p => p.Categoria == categoria);
        if (estrellas is >= 1 and <= 5)
        {
            var op = opCalif is "<=" or "=" or ">=" ? opCalif : ">=";
            var e = estrellas.Value;
            var promedios = db.Valoraciones
                .Where(v => v.PublicacionId != null && v.Activo && v.Calificacion != null)
                .GroupBy(v => v.PublicacionId!.Value)
                .Select(g => new { Id = g.Key, Prom = g.Average(v => v.Calificacion!.Value) });
            promedios = op switch
            {
                "<=" => promedios.Where(x => x.Prom <= e),
                "=" => e >= 5
                    ? promedios.Where(x => x.Prom == 5)
                    : promedios.Where(x => x.Prom >= e && x.Prom < e + 1),
                _ => promedios.Where(x => x.Prom >= e),
            };
            query = query.Where(p => promedios.Select(x => x.Id).Contains(p.Id));
        }
        const int porPagina = 10;
        int total;
        int totalPaginas;
        List<Publicacion> pubs;
        if (idsAlgolia is { Count: > 0 })
        {
            // Materializar y paginar respetando el ranking de Algolia, no la fecha.
            var orden = idsAlgolia.Select((id, indice) => (id, indice))
                .ToDictionary(x => x.id, x => x.indice);
            var coincidencias = (await query.ToListAsync())
                .OrderBy(p => orden.GetValueOrDefault(p.Id, int.MaxValue))
                .ToList();
            total = coincidencias.Count;
            totalPaginas = Math.Max(1, (int)Math.Ceiling(total / (double)porPagina));
            pagina = Math.Clamp(pagina, 1, totalPaginas);
            pubs = coincidencias.Skip((pagina - 1) * porPagina).Take(porPagina).ToList();
        }
        else
        {
            total = await query.CountAsync();
            totalPaginas = Math.Max(1, (int)Math.Ceiling(total / (double)porPagina));
            pagina = Math.Clamp(pagina, 1, totalPaginas);
            pubs = await query.OrderByDescending(p => p.FechaCreacion)
                .Skip((pagina - 1) * porPagina).Take(porPagina).ToListAsync();
        }
        var ids = pubs.Select(p => p.Id).ToList();
        ViewBag.CommentsCount = await db.ComentariosHilo
            .Where(c => ids.Contains(c.PublicacionId) && c.Activo)
            .GroupBy(c => c.PublicacionId)
            .ToDictionaryAsync(g => g.Key, g => g.Count());
        var autorIds = pubs.Select(p => p.AutorId).Distinct().ToList();
        _ = autorIds; // Reputación por usuario congelada; ahora se califica por publicación.
        ViewBag.Ratings = await db.Valoraciones
            .Where(v => v.PublicacionId != null && ids.Contains(v.PublicacionId.Value) && v.Activo && v.Calificacion != null)
            .GroupBy(v => v.PublicacionId!.Value)
            .Select(g => new { PublicacionId = g.Key, Total = g.Count(), Promedio = g.Average(v => v.Calificacion!.Value) })
            .ToDictionaryAsync(x => x.PublicacionId, x => (Promedio: x.Promedio, Total: x.Total));
        ViewBag.Seccion = seccion; ViewBag.Q = q; ViewBag.Ciudad = ciudad; ViewBag.Tipo = tipo;
        ViewBag.Modalidad = modalidad; ViewBag.Categoria = categoria;
        ViewBag.Estrellas = estrellas is >= 1 and <= 5 ? estrellas : null;
        ViewBag.OpCalif = opCalif is "<=" or "=" or ">=" ? opCalif : ">=";
        ViewBag.Pagina = pagina; ViewBag.TotalPaginas = totalPaginas; ViewBag.Total = total;
        return View(pubs);
    }

    [HttpGet]
    [Authorize]
    [Authorize(Roles = $"{Roles.Trabajador},{Roles.Empresa}")]
    public async Task<IActionResult> Create()
    {
        var yoId = users.GetUserId(User);
        ViewBag.CiudadAutor = yoId is null ? null : await ObtenerCiudadAutorAsync(yoId);
        return View(new PublicacionCreateVm());
    }

    private async Task<string?> ObtenerCiudadAutorAsync(string usuarioId)
    {
        var ciudad = await db.Perfiles
            .Where(p => p.UsuarioId == usuarioId)
            .Select(p => p.Ciudad)
            .FirstOrDefaultAsync();

        if (!string.IsNullOrWhiteSpace(ciudad)) return ciudad;

        return await db.Empresas
            .Where(e => e.UsuarioId == usuarioId)
            .Select(e => e.Ciudad)
            .FirstOrDefaultAsync();
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize]
    [CuentaActiva]
    [Authorize(Roles = $"{Roles.Trabajador},{Roles.Empresa}")]
    public async Task<IActionResult> Create(PublicacionCreateVm vm)
    {
        var yo = (await users.GetUserAsync(User))!;
        ViewBag.CiudadAutor = await ObtenerCiudadAutorAsync(yo.Id);
        if (!ModelState.IsValid) return View(vm);
        var permitePrecio = Catalogos.PermitePrecio(vm.Tipo);
        var permiteVacantes = Catalogos.PermiteVacantes(vm.Tipo);
        var habilidades = Catalogos.PermiteHabilidades(vm.Tipo) ? vm.HabilidadesReq : null;
        var textoModeracion = $"{vm.Titulo} {vm.Contenido} {habilidades}";
        var riesgo = moderacion.Evaluar(textoModeracion);
        if (moderacion.BloqueaPublicacion(riesgo))
        {
            ModelState.AddModelError(nameof(vm.Contenido),
                "La publicación no se publicó: el clasificador la marcó como contenido peligroso.");
            return View(vm);
        }
        string? imagen = null;
        if (vm.Imagen is not null && vm.Imagen.Length > 0)
        {
            var error = ImageUploadService.Validar(vm.Imagen);
            if (error is not null) { ModelState.AddModelError(nameof(vm.Imagen), error); return View(vm); }
            imagen = await uploads.GuardarAsync(vm.Imagen, "publicaciones");
        }
        var moneda = permitePrecio && string.Equals(vm.Moneda, Monedas.Usd, StringComparison.OrdinalIgnoreCase)
            ? Monedas.Usd
            : Monedas.Pen;
        var precio = permitePrecio ? Catalogos.PrecioParaGuardar(vm.PrecioTexto, moneda) : null;
        var pub = new Publicacion
        {
            AutorId = yo.Id, Tipo = vm.Tipo, Titulo = vm.Titulo,
            Contenido = vm.Contenido, HabilidadesReq = habilidades, Imagen = imagen,
            Categoria = string.IsNullOrWhiteSpace(vm.Categoria) ? null : vm.Categoria,
            Modalidad = string.IsNullOrWhiteSpace(vm.Modalidad) ? null : vm.Modalidad,
            PrecioTexto = precio,
            Moneda = moneda,
            PrecioDual = permitePrecio && vm.PrecioDual && precio is not null,
            Vacantes = permiteVacantes ? vm.Vacantes : null
        };
        db.Publicaciones.Add(pub);
        await db.SaveChangesAsync();
        await moderacion.RegistrarSiRiesgosoAsync("publicacion", pub.Id, yo.Id, riesgo);
        // Indexación externa (no bloquea ni rompe la creación si Algolia falla).
        await algolia.IndexPublicacionAsync(pub);
        TempData["Success"] = riesgo.Probabilidad >= SpamRiesgo.Umbral
            ? $"Publicación creada. Quedó en revisión automática ({riesgo.Clase})."
            : "Publicación creada.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var p = await db.Publicaciones
            .Include(x => x.Autor).ThenInclude(a => a!.Perfil)
            .Include(x => x.Autor).ThenInclude(a => a!.Empresa)
            .FirstOrDefaultAsync(x => x.Id == id && x.Activa);
        if (p is null) return NotFound();
        ViewBag.Comentarios = await db.ComentariosHilo
            .Include(c => c.Autor).ThenInclude(a => a!.Perfil)
            .Include(c => c.Autor).ThenInclude(a => a!.Empresa)
            .Where(c => c.PublicacionId == id && c.Activo && c.ParentId == null)
            .OrderByDescending(c => c.Fecha).Take(5).ToListAsync();
        ViewBag.TotalComentarios = await db.ComentariosHilo
            .CountAsync(c => c.PublicacionId == id && c.Activo);
        var rating = await db.Valoraciones
            .Where(v => v.PublicacionId == id && v.Activo && v.Calificacion != null)
            .GroupBy(v => v.PublicacionId)
            .Select(g => new { Total = g.Count(), Promedio = g.Average(v => v.Calificacion!.Value) })
            .FirstOrDefaultAsync();
        ViewBag.RatingPromedio = rating?.Promedio ?? 0;
        ViewBag.RatingTotal = rating?.Total ?? 0;
        // Lista de calificaciones con texto + sus respuestas (hilo por ParentId).
        var topVals = await db.Valoraciones
            .Include(v => v.Autor).ThenInclude(a => a!.Perfil)
            .Include(v => v.Autor).ThenInclude(a => a!.Empresa)
            .Where(v => v.PublicacionId == id && v.Activo && v.ParentId == null)
            .OrderByDescending(v => v.Fecha).Take(10).ToListAsync();
        var topIds = topVals.Select(v => v.Id).ToList();
        ViewBag.Valoraciones = topVals;
        ViewBag.RespuestasValoracion = await db.Valoraciones
            .Include(v => v.Autor).ThenInclude(a => a!.Perfil)
            .Include(v => v.Autor).ThenInclude(a => a!.Empresa)
            .Where(v => v.ParentId != null && topIds.Contains(v.ParentId.Value) && v.Activo)
            .OrderBy(v => v.Fecha).ToListAsync();
        int? miVoto = null;
        if (User.Identity?.IsAuthenticated == true)
        {
            var yoId = users.GetUserId(User);
            if (yoId is not null)
                miVoto = await db.Valoraciones
                    .Where(v => v.PublicacionId == id && v.AutorId == yoId && v.Activo)
                    .Select(v => v.Calificacion)
                    .FirstOrDefaultAsync();
        }
        ViewBag.MiCalificacion = miVoto;
        return View(p);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize, CuentaActiva]
    public async Task<IActionResult> Calificar(int id, int calificacion, string? comentario = null)
    {
        if (calificacion < 1 || calificacion > 5)
        {
            TempData["Error"] = "Calificación inválida (1 a 5 estrellas).";
            return RedirectToAction(nameof(Details), new { id });
        }
        var p = await db.Publicaciones.FirstOrDefaultAsync(x => x.Id == id && x.Activa);
        if (p is null) return NotFound();
        var yo = (await users.GetUserAsync(User))!;
        if (p.AutorId == yo.Id)
        {
            TempData["Warning"] = "No puedes calificar tu propia publicación.";
            return RedirectToAction(nameof(Details), new { id });
        }
        var texto = string.IsNullOrWhiteSpace(comentario) ? string.Empty : comentario.Trim();
        try
        {
            var existente = await db.Valoraciones
                .FirstOrDefaultAsync(v => v.PublicacionId == id && v.AutorId == yo.Id && v.ParentId == null);
            if (existente is null)
            {
                db.Valoraciones.Add(new ComentarioValoracion
                {
                    AutorId = yo.Id,
                    DestinatarioId = p.AutorId,
                    PublicacionId = id,
                    Calificacion = calificacion,
                    Comentario = texto,
                    ParentId = null,
                    Activo = true
                });
                TempData["Success"] = "Calificación guardada.";
            }
            else
            {
                existente.Calificacion = calificacion;
                if (!string.IsNullOrEmpty(texto)) existente.Comentario = texto;
                existente.Activo = true;
                existente.Fecha = DateTime.UtcNow;
                TempData["Success"] = "Calificación actualizada.";
            }
            await db.SaveChangesAsync();
            await reputacion.RecalcularAsync(p.AutorId);
        }
        catch (DbUpdateException)
        {
            TempData["Error"] = "Ya registraste tu calificación para esta publicación.";
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize, CuentaActiva]
    public async Task<IActionResult> ResponderValoracion(int valoracionId, string comentario)
    {
        var padre = await db.Valoraciones.FirstOrDefaultAsync(v => v.Id == valoracionId && v.Activo && v.ParentId == null);
        if (padre?.PublicacionId is null)
        { TempData["Error"] = "Calificación no encontrada."; return RedirectToAction(nameof(Index)); }
        if (string.IsNullOrWhiteSpace(comentario))
        { TempData["Warning"] = "Respuesta vacía."; return RedirectToAction(nameof(Details), new { id = padre.PublicacionId }); }
        var yo = (await users.GetUserAsync(User))!;
        db.Valoraciones.Add(new ComentarioValoracion
        {
            AutorId = yo.Id,
            DestinatarioId = padre.AutorId,
            PublicacionId = null, // hija: cuelga del padre, fuera del índice único
            Comentario = comentario.Trim(),
            ParentId = padre.Id,
            RespuestaAUsuarioId = padre.AutorId,
            Activo = true
        });
        await db.SaveChangesAsync();
        TempData["Success"] = "Respuesta publicada.";
        return RedirectToAction(nameof(Details), new { id = padre.PublicacionId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize, CuentaActiva]
    public async Task<IActionResult> Comment(int id, string comentario, int? parentId = null)
    {
        var yo = (await users.GetUserAsync(User))!;
        if (string.IsNullOrWhiteSpace(comentario)) { TempData["Warning"] = "Comentario vacío."; return RedirectToAction(nameof(Details), new { id }); }
        var texto = comentario.Trim();
        var riesgo = moderacion.Evaluar(texto);
        if (moderacion.BloqueaPublicacion(riesgo))
        {
            TempData["Error"] = "El comentario no se publicó: el clasificador lo marcó como contenido peligroso.";
            return RedirectToAction(nameof(Details), new { id });
        }
        var nuevo = new ComentarioPublicacion
        { PublicacionId = id, AutorId = yo.Id, Comentario = texto, ParentId = parentId };
        db.ComentariosHilo.Add(nuevo);
        await db.SaveChangesAsync();
        await moderacion.RegistrarSiRiesgosoAsync("comentario", nuevo.Id, yo.Id, riesgo);
        if (riesgo.Probabilidad >= SpamRiesgo.Umbral)
            TempData["Info"] = $"Comentario publicado. Quedó en revisión automática ({riesgo.Clase}).";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize, CuentaActiva]
    public async Task<IActionResult> Denunciar(int publicacionId, string motivo, string? descripcion = null)
    {
        var pub = await db.Publicaciones.FirstOrDefaultAsync(p => p.Id == publicacionId && p.Activa);
        if (pub is null) return NotFound();
        var yo = (await users.GetUserAsync(User))!;
        if (pub.AutorId == yo.Id)
        {
            TempData["Info"] = "No puedes denunciar tu propia publicación.";
            return RedirectToAction(nameof(Details), new { id = publicacionId });
        }
        return await RegistrarDenunciaAsync("publicacion", publicacionId, motivo, descripcion, publicacionId);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize, CuentaActiva]
    public async Task<IActionResult> DenunciarComentario(int comentarioId, int publicacionId, string motivo, string? descripcion = null)
    {
        var comentario = await db.ComentariosHilo.FirstOrDefaultAsync(c =>
            c.Id == comentarioId && c.Activo && c.PublicacionId == publicacionId);
        if (comentario is null) return NotFound();
        var yo = (await users.GetUserAsync(User))!;
        if (comentario.AutorId == yo.Id)
        {
            TempData["Info"] = "No puedes denunciar tu propio comentario.";
            return RedirectToAction(nameof(Details), new { id = publicacionId });
        }
        return await RegistrarDenunciaAsync("comentario", comentarioId, motivo, descripcion, publicacionId);
    }

    private async Task<IActionResult> RegistrarDenunciaAsync(
        string objetoTipo, int objetoId, string motivo, string? descripcion, int publicacionId)
    {
        var yo = (await users.GetUserAsync(User))!;
        var motivoNormalizado = (motivo ?? string.Empty).Trim().ToLowerInvariant();
        string[] motivos = { "spam", "estafa", "ofensivo", "peligroso", "falso", "basura", "otro" };
        if (!motivos.Contains(motivoNormalizado))
        {
            TempData["Warning"] = "Seleccione un motivo válido.";
            return RedirectToAction(nameof(Details), new { id = publicacionId });
        }
        var yaExiste = await db.Denuncias.AnyAsync(d =>
            d.DenuncianteId == yo.Id && d.ObjetoTipo == objetoTipo &&
            d.ObjetoId == objetoId && d.Estado == "pendiente");
        if (yaExiste)
        {
            TempData["Info"] = "Ya reportaste este contenido. Está pendiente de revisión.";
            return RedirectToAction(nameof(Details), new { id = publicacionId });
        }
        var detalle = string.IsNullOrWhiteSpace(descripcion) ? null : descripcion.Trim();
        if (detalle is { Length: > 500 }) detalle = detalle[..500];
        db.Denuncias.Add(new Denuncia
        {
            DenuncianteId = yo.Id,
            ObjetoTipo = objetoTipo,
            ObjetoId = objetoId,
            Motivo = motivoNormalizado,
            Descripcion = detalle,
            Estado = "pendiente"
        });
        await db.SaveChangesAsync();
        TempData["Success"] = "Denuncia enviada. El equipo de moderación la revisará.";
        return RedirectToAction(nameof(Details), new { id = publicacionId });
    }
}
