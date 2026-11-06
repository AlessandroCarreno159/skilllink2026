using System.Text;
using Microsoft.EntityFrameworkCore;
using SkillLink_dotnet.Models;
using SkillLink_dotnet.Services.Ml;

namespace SkillLink_dotnet.Data;

/// <summary>
/// Siembra controlada para dejar 80 publicaciones válidas y 70 riesgosas
/// (30 spam, 20 basura y 20 peligroso), conservando las publicaciones existentes.
/// </summary>
public static class PublicacionSeed
{
    private static readonly Dictionary<string, int> Objetivos = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Normal"] = 80,
        ["Spam"] = 30,
        ["Basura"] = 20,
        ["Peligroso"] = 20
    };

    private static readonly string[] Imagenes =
    [
        "publicaciones/pub_2.jpg", "publicaciones/pub_3.jpg", "publicaciones/pub_5.jpg",
        "publicaciones/pub_6.jpg", "publicaciones/pub_7.jpg", "publicaciones/pub_8.jpg",
        "publicaciones/pub_9.jpg", "publicaciones/pub_10.jpg", "publicaciones/pub_11.jpg",
        "publicaciones/pub_12.jpg", "publicaciones/pub_13.jpg"
    ];

    private static readonly (string Rol, string Categoria, string Habilidades, string Tarea)[] Roles =
    [
        ("albañil", "construccion", "albañilería, lectura de planos", "realizar tarrajeo, vaciado y acabados"),
        ("electricista", "tecnologia", "electricidad, tableros", "mantenimiento eléctrico preventivo"),
        ("gasfitero", "hogar", "gasfitería, redes sanitarias", "instalación y reparación de redes de agua"),
        ("soldador", "construccion", "soldadura MIG, TIG", "fabricación de estructuras metálicas"),
        ("diseñador gráfico", "diseno", "Photoshop, Illustrator", "piezas gráficas y branding"),
        ("desarrollador web", "tecnologia", "C#, JavaScript, SQL", "desarrollo de módulos web"),
        ("recepcionista", "otros", "atención al cliente, Excel", "atención en recepción y agenda"),
        ("cocinero", "hogar", "cocina criolla, higiene", "preparación de menú diario"),
        ("personal de limpieza", "hogar", "limpieza, desinfección", "limpieza de oficinas y áreas comunes"),
        ("asistente administrativo", "otros", "Excel, redacción", "registro y control documentario"),
        ("mecánico automotriz", "tecnologia", "diagnóstico, mantenimiento", "mantenimiento preventivo de vehículos"),
        ("pintor", "construccion", "pintura, acabados", "pintura interior y exterior")
    ];

    private static readonly string[] Ciudades =
    [
        "Lima", "Arequipa", "Trujillo", "Cusco", "Piura", "Chiclayo",
        "Huancayo", "Ica", "Tacna", "Puno", "Ayacucho", "Cajamarca"
    ];

    private static readonly string[] SpamTextos =
    [
        "Gana S/500 diarios desde casa, inscríbete ya.",
        "Trabajo urgente solo para mayores de edad, escribe al WhatsApp 999888777.",
        "Inversión garantizada: duplica tu dinero en solo 7 días.",
        "Oferta exclusiva: paga S/200 de adelanto para reservar tu cupo.",
        "Reclutamos personal sin experiencia, solo deposita S/50 por inscripción.",
        "Gana dinero rápido con tu celular sin salir de casa.",
        "Haz clic en el enlace y reclama tu bono de bienvenida.",
        "Préstamo inmediato sin requisitos ni aval.",
        "Envía tu yape al 987654321 y empieza a ganar hoy.",
        "Únete al grupo de Telegram para trabajos desde casa.",
        "Solo hoy, oferta relámpago, inscríbete por WhatsApp.",
        "Trabajo desde casa, gana en dólares sin experiencia.",
        "Gana S/300 al día solo con tu celular.",
        "Inscríbete al curso gratuito y recibe un bono de S/100.",
        "Oportunidad única, cupos limitados, escribe al WhatsApp.",
        "Inversión segura con ganancias garantizadas.",
        "Paga tu membresía y accede a trabajos exclusivos.",
        "Deposita S/30 y activa tu cuenta de trabajo.",
        "Gana dinero desde casa con solo 2 horas al día.",
        "Reclutamos personas para trabajar desde el celular.",
        "Oferta de empleo inmediata, solo mayores de edad.",
        "Escríbenos al privado para enviarte el enlace de inscripción.",
        "Trabajo urgente, pago diario, no necesitas experiencia.",
        "Únete al grupo VIP de ofertas laborales.",
        "Gana comisiones ilimitadas invitando a más personas.",
        "Invierte hoy y recibe el doble mañana.",
        "Solicita tu préstamo por WhatsApp sin trámites.",
        "Cupos limitados para trabajar desde casa.",
        "Descarga la aplicación y gana dinero en minutos.",
        "Solo deposita S/20 y empieza a trabajar hoy.",
        "Oferta exclusiva para mayores de 18 años.",
        "Envía un mensaje al 999888777 y recibe tu bono.",
        "Trabajo garantizado con pago semanal.",
        "Gana dinero extra sin salir de casa.",
        "Inscripción gratuita, solo paga el kit de trabajo.",
        "Hola, te comparto una oportunidad para ganar desde casa.",
        "Trabaja desde tu celular y recibe pagos por Yape.",
        "Accede al enlace y reclama tu premio.",
        "Únete a nuestro equipo y gana comisiones.",
        "Oferta relámpago solo por hoy."
    ];

    private static readonly string[] BasuraTextos =
    [
        "busco algo", "mi perro sabe volar", "busco empleo de cualquier cosa",
        "texto de prueba", "el cielo es verde", "no hay trabajo", "$$$$",
        "trabajo trabajo trabajo", "me aburro", ".", "necesito algo",
        "sin información", "quiero pega", "busco pega urgente",
        "busco trabajo cualquier cosa", "????????", "123456789", "busco trabajo",
        "nada que ver", "quiero trabajo", "hola", "asdf", "no sé",
        "cualquier cosa", "busco"
    ];

    private static readonly string[] PeligrosoTextos =
    [
        "voy a publicar tus fotos íntimas",
        "te voy a dar una paliza",
        "menor de edad busca pareja",
        "si te alejas te va a ir peor",
        "te voy a secuestrar",
        "odio a los inmigrantes y los voy a atacar",
        "tráfico de personas",
        "voy a doxxearte",
        "si no me pagas te expongo",
        "te voy a golpear hasta que no puedas moverte",
        "voy a destruir tu reputación con mentiras",
        "cuídate las espaldas",
        "envíame fotos sin ropa",
        "si no colaboras atente a las consecuencias",
        "me quiero matar y culpo a ustedes",
        "mejor no salgas de tu casa",
        "te voy a quitar todo",
        "voy a matar a tu perro",
        "vendo cocaína",
        "hay que eliminar a los homosexuales",
        "te voy a encontrar y vas a pagar",
        "sé dónde vives y te tengo ubicado",
        "voy a quemar tu casa",
        "te voy a hacer daño",
        "tengo un arma y no me importa usarla"
    ];

    public static async Task EjecutarAsync(
        ApplicationDbContext db,
        SpamClasificadorService clasificador,
        bool dryRun,
        CancellationToken cancellationToken = default)
    {
        var existentes = await db.Publicaciones.ToListAsync(cancellationToken);
        var conteo = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Normal"] = 0, ["Spam"] = 0, ["Basura"] = 0, ["Peligroso"] = 0
        };
        var riesgosExistentes = new List<(Publicacion Publicacion, RiesgoContenido Riesgo)>();
        foreach (var publicacion in existentes)
        {
            var riesgo = clasificador.Predecir(Texto(publicacion));
            conteo[riesgo.Clase] = conteo.GetValueOrDefault(riesgo.Clase) + 1;
            if (!string.Equals(riesgo.Clase, "Normal", StringComparison.OrdinalIgnoreCase))
                riesgosExistentes.Add((publicacion, riesgo));
        }

        Console.WriteLine($"Modo clasificador: {clasificador.Modo}");
        Console.WriteLine($"Publicaciones existentes: {existentes.Count}");
        foreach (var par in conteo.OrderBy(x => x.Key))
            Console.WriteLine($"  {par.Key}: {par.Value}");

        if (existentes.Count > 150)
            throw new InvalidOperationException($"Ya existen {existentes.Count} publicaciones; se esperaban como máximo 150.");

        var faltantes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (clase, objetivo) in Objetivos)
        {
            var actual = conteo.GetValueOrDefault(clase);
            if (actual > objetivo)
                throw new InvalidOperationException($"Ya hay {actual} publicaciones de clase {clase}; el objetivo es {objetivo}.");
            faltantes[clase] = objetivo - actual;
        }

        var totalFaltante = faltantes.Values.Sum();
        Console.WriteLine($"Faltantes para 150: {totalFaltante} " +
                          $"({faltantes["Normal"]} normal, {faltantes["Spam"]} spam, " +
                          $"{faltantes["Basura"]} basura, {faltantes["Peligroso"]} peligroso).");

        if (totalFaltante == 0)
        {
            Console.WriteLine("La base ya cumple la distribución objetivo.");
            return;
        }

        var autores = await db.Publicaciones.AsNoTracking()
            .Select(p => p.AutorId)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (autores.Count == 0)
        {
            autores = await db.Users.AsNoTracking()
                .Where(u => u.Rol != SkillLink_dotnet.Models.Roles.Admin)
                .OrderBy(u => u.Id)
                .Select(u => u.Id)
                .Take(6)
                .ToListAsync(cancellationToken);
        }
        if (autores.Count == 0)
            throw new InvalidOperationException("No hay usuarios para asignar como autores.");

        var titulosExistentes = existentes
            .Select(p => p.Titulo ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var validos = ConstruirValidos()
            .Where(c => !titulosExistentes.Contains(c.Titulo))
            .ToList();
        var spamTextos = SpamTextos
            .Concat(CargarTextosCsv("ml_spam_seed.csv"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var basuraTextos = BasuraTextos
            .Concat(CargarTextosCsv("ml_basura_seed.csv"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var peligrosoTextos = PeligrosoTextos
            .Concat(CargarTextosCsv("ml_peligroso_seed.csv"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var spam = ConstruirRiesgosos("Spam", spamTextos, 0)
            .Where(c => !titulosExistentes.Contains(c.Titulo))
            .ToList();
        var basura = ConstruirRiesgosos("Basura", basuraTextos, 1000)
            .Where(c => !titulosExistentes.Contains(c.Titulo))
            .ToList();
        var peligroso = ConstruirRiesgosos("Peligroso", peligrosoTextos, 2000)
            .Where(c => !titulosExistentes.Contains(c.Titulo))
            .ToList();

        var rechazos = new List<string>();
        var seleccionValidos = Seleccionar(validos, faltantes["Normal"], "Normal", clasificador, rechazos);
        var seleccionSpam = Seleccionar(spam, faltantes["Spam"], "Spam", clasificador, rechazos);
        var seleccionBasura = Seleccionar(basura, faltantes["Basura"], "Basura", clasificador, rechazos);
        var seleccionPeligroso = Seleccionar(peligroso, faltantes["Peligroso"], "Peligroso", clasificador, rechazos);

        Console.WriteLine($"Candidatos validados: {seleccionValidos.Count + seleccionSpam.Count + seleccionBasura.Count + seleccionPeligroso.Count}.");
        if (rechazos.Count > 0)
            Console.WriteLine($"Candidatos descartados por clase inesperada: {rechazos.Count}.");

        if (dryRun)
        {
            Console.WriteLine("SIMULACIÓN: no se escribió nada en la base de datos.");
            foreach (var (publicacion, riesgo) in riesgosExistentes.Where(x => x.Publicacion.Activa))
                Console.WriteLine($"  Se ocultaría la publicación existente {publicacion.Id} ({riesgo.Clase}).");
            return;
        }

        var estrategia = db.Database.CreateExecutionStrategy();
        await estrategia.ExecuteAsync(async () =>
        {
        await using var transaccion = await db.Database.BeginTransactionAsync(cancellationToken);

        foreach (var (publicacion, _) in riesgosExistentes)
        {
            if (publicacion.Activa) publicacion.Activa = false;
        }

        var nuevas = new List<(Publicacion Publicacion, string Clase, RiesgoContenido Riesgo)>();
        var indiceImagen = 0;
        for (var i = 0; i < seleccionValidos.Count; i++)
        {
            var candidato = seleccionValidos[i];
            var publicacion = CrearPublicacion(
                candidato,
                autores[i % autores.Count],
                activa: true,
                Imagenes[indiceImagen++ % Imagenes.Length],
                DateTime.UtcNow.AddMinutes(-10 - i * 7));
            db.Publicaciones.Add(publicacion);
            nuevas.Add((publicacion, "Normal", clasificador.Predecir(Texto(candidato))));
        }

        var autorRiesgo = autores[^1];
        var riesgos = seleccionSpam.Select(c => (Candidato: c, Clase: "Spam"))
            .Concat(seleccionBasura.Select(c => (Candidato: c, Clase: "Basura")))
            .Concat(seleccionPeligroso.Select(c => (Candidato: c, Clase: "Peligroso")))
            .ToList();
        for (var i = 0; i < riesgos.Count; i++)
        {
            var (candidato, clase) = riesgos[i];
            var publicacion = CrearPublicacion(
                candidato,
                autorRiesgo,
                activa: false,
                imagen: null,
                DateTime.UtcNow.AddDays(-1).AddMinutes(-i));
            db.Publicaciones.Add(publicacion);
            nuevas.Add((publicacion, clase, clasificador.Predecir(Texto(candidato))));
        }

        await db.SaveChangesAsync(cancellationToken);

        foreach (var (publicacion, clase, riesgo) in nuevas.Where(x => x.Clase != "Normal"))
        {
            db.Denuncias.Add(CrearDenuncia(publicacion, clase, riesgo));
        }

        foreach (var (publicacion, riesgo) in riesgosExistentes)
        {
            var yaExiste = await db.Denuncias.AnyAsync(d =>
                d.ObjetoTipo == "publicacion" &&
                d.ObjetoId == publicacion.Id &&
                d.Estado == "pendiente", cancellationToken);
            if (!yaExiste)
                db.Denuncias.Add(CrearDenuncia(publicacion, riesgo.Clase, riesgo));
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaccion.CommitAsync(cancellationToken);

        Console.WriteLine($"Publicaciones nuevas: {nuevas.Count}.");
        Console.WriteLine($"Total esperado: {existentes.Count + nuevas.Count}.");
        Console.WriteLine($"Denuncias pendientes nuevas: {nuevas.Count(x => x.Clase != "Normal") + riesgosExistentes.Count}.");
        Console.WriteLine("Seed 150 aplicado.");
        });
    }

    private static List<Candidato> ConstruirValidos()
    {
        var lista = new List<Candidato>();
        var indice = 0;
        foreach (var ciudad in Ciudades)
        {
            foreach (var rol in Roles)
            {
                var tipo = (indice % 5) switch
                {
                    0 => "oferta",
                    1 => "convocatoria",
                    2 => "experiencia",
                    3 => "busqueda",
                    _ => "proyecto"
                };
                var titulo = tipo switch
                {
                    "experiencia" => $"Mi experiencia como {rol.Rol} en {ciudad}",
                    "busqueda" => $"Busco trabajo como {rol.Rol} en {ciudad}",
                    "proyecto" => $"Proyecto de {rol.Rol} en {ciudad}",
                    "convocatoria" => $"Convocatoria para {rol.Rol} en {ciudad}",
                    _ => $"Se busca {rol.Rol} en {ciudad}"
                };
                var contenido = tipo switch
                {
                    "experiencia" =>
                        $"Comparto mi experiencia como {rol.Rol} en {ciudad}. Durante los últimos años me dediqué a {rol.Tarea}, cuidando la seguridad y la calidad del trabajo. Busco nuevos clientes o proyectos en la ciudad.",
                    "busqueda" =>
                        $"Soy {rol.Rol} con experiencia en {rol.Habilidades}. Busco trabajo en {ciudad} para {rol.Tarea}. Tengo disponibilidad para iniciar y referencias verificables.",
                    "proyecto" =>
                        $"Proyecto de {rol.Rol} en {ciudad}: se requiere apoyo para {rol.Tarea}. El trabajo se coordina por etapas y se paga contra avance.",
                    "convocatoria" =>
                        $"Convocatoria para {rol.Rol} en {ciudad}. La persona seleccionada se encargará de {rol.Tarea}. Valoramos puntualidad, responsabilidad y trabajo en equipo.",
                    _ =>
                        $"Se busca {rol.Rol} en {ciudad} para {rol.Tarea}. Ofrecemos contrato formal, buen ambiente laboral y capacitación inicial."
                };
                var precio = tipo is "experiencia" or "busqueda" ? null : $"S/{(1200 + indice * 10)} - S/{(1600 + indice * 10)}";
                var vacantes = tipo is "experiencia" or "busqueda" ? (int?)null : (indice % 4) + 1;
                var modalidad = (indice % 3) switch
                {
                    0 => "tiempo_completo",
                    1 => "medio_tiempo",
                    _ => "por_proyecto"
                };
                lista.Add(new Candidato(
                    "Normal", tipo, titulo, contenido, rol.Habilidades, rol.Categoria,
                    modalidad, precio, vacantes, Imagenes[indice % Imagenes.Length]));
                indice++;
            }
        }
        return lista;
    }

    private static string[] CargarTextosCsv(string archivo)
    {
        var ruta = Path.Combine(AppContext.BaseDirectory, "Data", archivo);
        if (!File.Exists(ruta)) return [];
        return File.ReadLines(ruta, Encoding.UTF8)
            .Skip(1)
            .Where(linea => linea.EndsWith(",true", StringComparison.OrdinalIgnoreCase))
            .Select(linea => linea[..^5].Trim().Trim('"'))
            .Where(texto => texto.Length > 0)
            .ToArray();
    }

    private static List<Candidato> ConstruirRiesgosos(string clase, string[] textos, int offset)
        => textos.Select((texto, i) => new Candidato(
            clase,
            "oferta",
            $"Publicación SEED150-{offset + i + 1:000}",
            texto,
            null,
            "otros",
            null,
            null,
            null,
            null)).ToList();

    private static List<Candidato> Seleccionar(
        IEnumerable<Candidato> candidatos,
        int cantidad,
        string clase,
        SpamClasificadorService clasificador,
        List<string> rechazos)
    {
        var seleccionados = new List<Candidato>();
        foreach (var candidato in candidatos)
        {
            var riesgo = clasificador.Predecir(Texto(candidato));
            if (string.Equals(riesgo.Clase, clase, StringComparison.OrdinalIgnoreCase))
            {
                seleccionados.Add(candidato);
                if (seleccionados.Count == cantidad) return seleccionados;
            }
            else
            {
                rechazos.Add($"{candidato.Titulo}: esperado {clase}, obtenido {riesgo.Clase}");
            }
        }

        throw new InvalidOperationException(
            $"No hay suficientes candidatos {clase}: se necesitan {cantidad} y solo se validaron {seleccionados.Count}.");
    }

    private static Publicacion CrearPublicacion(
        Candidato candidato,
        string autorId,
        bool activa,
        string? imagen,
        DateTime fechaCreacion) => new()
    {
        AutorId = autorId,
        Tipo = candidato.Tipo,
        Titulo = candidato.Titulo,
        Contenido = candidato.Contenido,
        HabilidadesReq = candidato.HabilidadesReq,
        Categoria = candidato.Categoria,
        Modalidad = candidato.Modalidad,
        PrecioTexto = candidato.PrecioTexto,
        Vacantes = candidato.Vacantes,
        Imagen = imagen,
        Activa = activa,
        FechaCreacion = fechaCreacion
    };

    private static Denuncia CrearDenuncia(Publicacion publicacion, string clase, RiesgoContenido riesgo) => new()
    {
        DenuncianteId = DenunciasSistema.Id,
        ObjetoTipo = "publicacion",
        ObjetoId = publicacion.Id,
        Motivo = clase.ToLowerInvariant(),
        Descripcion =
            $"Detección automática SEED150: {clase} {riesgo.Probabilidad:P0} " +
            $"(spam {riesgo.ProbabilidadSpam:P0}, peligroso {riesgo.ProbabilidadPeligroso:P0}, " +
            $"basura {riesgo.ProbabilidadBasura:P0}).",
        Estado = "pendiente",
        Fecha = DateTime.UtcNow
    };

    private static string Texto(Publicacion publicacion)
        => $"{publicacion.Titulo} {publicacion.Contenido} {publicacion.HabilidadesReq}";

    private static string Texto(Candidato candidato)
        => $"{candidato.Titulo} {candidato.Contenido} {candidato.HabilidadesReq}";

    private sealed record Candidato(
        string Clase,
        string Tipo,
        string Titulo,
        string Contenido,
        string? HabilidadesReq,
        string? Categoria,
        string? Modalidad,
        string? PrecioTexto,
        int? Vacantes,
        string? Imagen);
}
