using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SkillLink_dotnet.Data;
using SkillLink_dotnet.Models;
using SkillLink_dotnet.Services;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// Render (y cualquier proxy TLS) termina HTTPS fuera del contenedor:
// se confía en X-Forwarded-Proto para no generar loops http→https.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});

// MySQL + EF Core (Pomelo 9 + EF Core 9 sobre net10.0; ver README).
// NOTA compatibilidad: Pomelo.EntityFrameworkCore.MySql 10 aún estaba WIP,
// por eso se usa Pomelo 9.0.0 + EF Core 9.0.x, estable y compatible con .NET 10.
// Se usa versión fija (no AutoDetect) para que `dotnet ef` funcione sin MySQL en vivo.
// Ajuste la versión a la de su servidor (MySQL 8.0/8.4 o MariaDB) en appsettings "MySql:ServerVersion".
var conn = builder.Configuration.GetConnectionString("DefaultConnection");
var serverVersionString = builder.Configuration["MySql:ServerVersion"] ?? "8.0.36";
var serverVersion = Microsoft.EntityFrameworkCore.ServerVersion.Parse(serverVersionString);
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySql(conn, serverVersion, mySql =>
        mySql.EnableRetryOnFailure(maxRetryCount: 3)));

builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.User.RequireUniqueEmail = false;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
});

builder.Services.AddScoped<ReputacionService>();
builder.Services.AddScoped<NotificacionService>();
builder.Services.AddHttpClient();
builder.Services.AddScoped<ImageUploadService>();
// Hasher que acepta los bcrypt del sistema Flask original y los migra a PBKDF2 al hacer login.
builder.Services.AddScoped<IPasswordHasher<ApplicationUser>, LegacyPasswordHasher>();
builder.Services.AddControllersWithViews();
builder.Services.AddSingleton<SkillLink.Services.AlgoliaService>();

var app = builder.Build();

app.UseForwardedHeaders();

// Migraciones solo cuando se pide (primer deploy en Render con RUN_MIGRATIONS=true).
// La imagen runtime no trae `dotnet ef`, por eso se migran desde la app.
if (app.Configuration.GetValue<bool>("RUN_MIGRATIONS"))
{
    using var scope = app.Services.CreateScope();
    var dbm = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await dbm.Database.MigrateAsync();
    app.Logger.LogInformation("Migraciones aplicadas (RUN_MIGRATIONS=true).");
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
// Sirve wwwroot, incluyendo archivos subidos en tiempo de ejecución (uploads/).
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

// Claims extra (Rol/EstadoCuenta) para el filtro CuentaActiva y el layout.
app.Use(async (ctx, next) =>
{
    if (ctx.User?.Identity?.IsAuthenticated == true && !ctx.User.HasClaim(c => c.Type == "EstadoCuenta"))
    {
        var um = ctx.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var u = await um.GetUserAsync(ctx.User);
        if (u is not null)
        {
            var identity = (ClaimsIdentity)ctx.User.Identity!;
            identity.AddClaim(new Claim("EstadoCuenta", u.EstadoCuenta));
            identity.AddClaim(new Claim("Rol", u.Rol));
            identity.AddClaim(new Claim("Dni", u.Dni));
        }
    }
    await next();
});

try
{
    await IdentitySeed.EnsureAsync(app.Services, app.Configuration);
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "Seed inicial omitido: no se pudo conectar a MySQL. La app arranca igual.");
}

// Reindexado único a Algolia: `dotnet run -- --reindex` (indexa las activas y sale).
if (args.Contains("--reindex"))
{
    using var scope = app.Services.CreateScope();
    var dbr = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var algoliaSvc = scope.ServiceProvider.GetRequiredService<SkillLink.Services.AlgoliaService>();
    if (!algoliaSvc.IsConfigured)
    {
        app.Logger.LogError("Reindex abortado: Algolia sin configurar (ApplicationId/ApiKey).");
        return;
    }
    var todas = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
        .ToListAsync(dbr.Publicaciones.Where(p => p.Activa));
    var ok = 0;
    foreach (var pub in todas)
    {
        try { await algoliaSvc.IndexPublicacionAsync(pub); ok++; }
        catch (Exception ex) { app.Logger.LogWarning(ex, "No se indexó {Id}.", pub.Id); }
    }
    app.Logger.LogInformation("Reindexadas {Ok}/{Total} publicaciones en Algolia.", ok, todas.Count);
    return;
}
// Datos demo (desarrollo): `dotnet run -- --seed-demo` crea 2 usuarios demo
// aprobados + 8 publicaciones con las imágenes semilla e indexa a Algolia.
// Idempotente: si los DNIs demo existen, no duplica.
if (args.Contains("--seed-demo"))
{
    using var scope = app.Services.CreateScope();
    var sp = scope.ServiceProvider;
    var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
    var dbs = sp.GetRequiredService<ApplicationDbContext>();
    var algoliaSeed = sp.GetRequiredService<SkillLink.Services.AlgoliaService>();

    async Task<ApplicationUser> EnsureDemoUserAsync(string dni, string email, string rol)
    {
        var u = await userManager.FindByNameAsync(dni);
        if (u is not null) return u;
        u = new ApplicationUser
        {
            UserName = dni, Dni = dni, Email = email, Rol = rol,
            EstadoCuenta = EstadosCuenta.Aprobado, PerfilValidado = true, EmailConfirmed = true
        };
        var res = await userManager.CreateAsync(u, "Demo12345!");
        if (!res.Succeeded) throw new Exception("No se creó demo " + dni + ": " +
            string.Join("; ", res.Errors.Select(e => e.Description)));
        await userManager.AddToRoleAsync(u, rol);
        return u;
    }

    var emp = await EnsureDemoUserAsync("20000001", "demo.empresa@skilllink.local", Roles.Empresa);
    var tra = await EnsureDemoUserAsync("20000002", "demo.trabajador@skilllink.local", Roles.Trabajador);
    var tra2 = await EnsureDemoUserAsync("20000003", "demo.disenadora@skilllink.local", Roles.Trabajador);
    var emp2 = await EnsureDemoUserAsync("20000004", "demo.servicios@skilllink.local", Roles.Empresa);

    if (await dbs.Empresas.AllAsync(e => e.UsuarioId != emp.Id))
        dbs.Empresas.Add(new Empresa
        {
            UsuarioId = emp.Id, RazonSocial = "Constructora Los Andes S.A.C.",
            Ruc = "20123456789", NombreContacto = "María Torres",
            Ciudad = "Lima", Telefono = "987654321", Sector = "Construcción",
            Descripcion = "Empresa constructora con 15 años en Lima.",
            Logo = "fotos/logo_emp_11.jpg"
        });
    if (await dbs.Perfiles.AllAsync(p => p.UsuarioId != tra.Id))
        dbs.Perfiles.Add(new Perfil
        {
            UsuarioId = tra.Id, NombreCompleto = "Juan Carlos Mamani Quispe",
            Ciudad = "Arequipa", Telefono = "976543210", Especialidad = "Gasfitería y electricidad",
            Descripcion = "Técnico con 10 años de experiencia.",
            Habilidades = "Gasfitería, electricidad, soldadura",
            Disponibilidad = "disponible", Foto = "fotos/avatar_15.jpg"
        });
    if (await dbs.Perfiles.AllAsync(p => p.UsuarioId != tra2.Id))
        dbs.Perfiles.Add(new Perfil
        {
            UsuarioId = tra2.Id, NombreCompleto = "Rosa Elena Quispe Huamán",
            Ciudad = "Lima", Telefono = "965432109", Especialidad = "Diseño gráfico",
            Descripcion = "Diseñadora freelance, branding y redes sociales.",
            Habilidades = "Photoshop, Illustrator, redes sociales",
            Disponibilidad = "disponible", Foto = "fotos/avatar_16.jpg"
        });
    if (await dbs.Empresas.AllAsync(e => e.UsuarioId != emp2.Id))
        dbs.Empresas.Add(new Empresa
        {
            UsuarioId = emp2.Id, RazonSocial = "Servicios Generales El Sol E.I.R.L.",
            Ruc = "20987654321", NombreContacto = "Pedro Solís",
            Ciudad = "Trujillo", Telefono = "954321098", Sector = "Servicios",
            Descripcion = "Limpieza y mantenimiento de oficinas.",
            Logo = "fotos/logo_emp_12.jpg"
        });
    await dbs.SaveChangesAsync();

    // Antecedentes demo (como los del registro real).
    foreach (var u in new[] { emp, tra, tra2, emp2 })
        if (!await dbs.Antecedentes.AnyAsync(a => a.UsuarioId == u.Id))
            dbs.Antecedentes.Add(new Antecedente
            {
                UsuarioId = u.Id, TieneAntecedentes = false,
                Declaracion = "Declaro no tener antecedentes penales ni judiciales.",
                Revisado = true, ValidadoAdmin = true
            });
    await dbs.SaveChangesAsync();

    // Experiencia laboral demo.
    if (!await dbs.Experiencias.AnyAsync(e => e.UsuarioId == tra.Id))
    {
        dbs.Experiencias.Add(new ExperienciaLaboral
        { UsuarioId = tra.Id, LugarTrabajo = "Pollería El Sabor", CargoOficio = "Ayudante de cocina", TiempoTrabajado = "2 años", DescripcionActividades = "Apoyo en cocina y atención al público." });
        dbs.Experiencias.Add(new ExperienciaLaboral
        { UsuarioId = tra.Id, LugarTrabajo = "Independiente", CargoOficio = "Gasfitero", TiempoTrabajado = "5 años", DescripcionActividades = "Instalaciones sanitarias en Arequipa." });
    }
    if (!await dbs.Experiencias.AnyAsync(e => e.UsuarioId == tra2.Id))
        dbs.Experiencias.Add(new ExperienciaLaboral
        { UsuarioId = tra2.Id, LugarTrabajo = "Estudio Creativo Prisma", CargoOficio = "Diseñadora gráfica", TiempoTrabajado = "3 años", DescripcionActividades = "Branding y piezas para redes sociales." });
    await dbs.SaveChangesAsync();

    var demos = new (string AutorId, string Tipo, string Titulo, string Contenido, string? Hab, string? Cat, string? Mod, string? Precio, int? Vac, string? Img)[]
    {
        (emp.Id, "oferta", "Se busca maestro de obra para edificio en Miraflores",
         "Obra de 6 pisos. Se requiere experiencia mínima de 5 años y disponibilidad inmediata.",
         "Albañilería, lectura de planos", "construccion", "tiempo_completo", "S/1800 - S/2200", 3, "publicaciones/pub_2.jpg"),
        (emp.Id, "oferta", "Técnico electricista para mantenimiento industrial",
         "Mantenimiento preventivo de tableros y motores en planta del Callao.",
         "Electricidad industrial, tableros", "tecnologia", "por_proyecto", "S/90 - S/150", 2, "publicaciones/pub_3.jpg"),
        (emp.Id, "convocatoria", "Convocatoria: diseñador gráfico freelance",
         "Diseño de piezas para redes sociales, trabajo remoto por proyecto.",
         "Photoshop, Illustrator", "diseno", "por_proyecto", "S/500 - S/800", 1, "publicaciones/pub_4.jpg"),
        (tra.Id, "experiencia", "Mi experiencia como gasfitero en Arequipa",
         "Comparto fotos de instalaciones sanitarias realizadas en Yanahuara y Cayma.",
         "Gasfitería, sanitarios", "hogar", null, null, null, "publicaciones/pub_5.jpg"),
        (emp.Id, "oferta", "Niñera por horas en Surco",
         "Cuidado de 2 niños por las tardes, de lunes a viernes.",
         "Puericultura, primeros auxilios", "hogar", "medio_tiempo", "S/20 - S/30", 1, "publicaciones/pub_6.jpg"),
        (tra.Id, "busqueda", "Busco trabajo como ayudante de cocina",
         "Tengo experiencia en pollerías y menú criollo. Disponibilidad inmediata en Lima.",
         "Cocina criolla, atención", "otros", "tiempo_completo", null, null, "publicaciones/pub_7.jpg"),
        (emp.Id, "oferta", "Soldador con experiencia en estructuras metálicas",
         "Soldadura MIG y TIG para taller en Ate. Se ofrece planilla.",
         "Soldadura MIG, TIG", "construccion", "tiempo_completo", "S/1500 - S/1800", 2, "publicaciones/pub_8.jpg"),
        (emp.Id, "proyecto", "Proyecto: instalación de red de agua en condominio",
         "Instalación completa de red sanitaria en condominio de 40 viviendas en Comas.",
         "Gasfitería, redes sanitarias", "construccion", "por_proyecto", "S/2500 - S/4000", 4, "publicaciones/pub_9.jpg"),
        (tra2.Id, "experiencia", "Portafolio: branding para cafeterías en Lima",
         "Les comparto parte de mi portafolio: logos y empaques para 3 cafeterías de Miraflores.",
         "Branding, empaques", "diseno", null, null, null, "publicaciones/pub_10.jpg"),
        (emp2.Id, "oferta", "Se necesita personal de limpieza para oficinas",
         "Limpieza de oficinas en el centro de Trujillo, turno mañana.",
         "Limpieza, mantenimiento", "hogar", "medio_tiempo", "S/1130", 2, "publicaciones/pub_11.jpg"),
        (tra2.Id, "proyecto", "Proyecto: rediseño de carta para restaurante",
         "Rediseño integral de carta y señalética para restaurante criollo.",
         "Diseño editorial, fotografía", "diseno", "por_proyecto", "S/300 - S/600", 1, "publicaciones/pub_12.jpg"),
        (emp.Id, "oferta", "Ayudantes de albañilería para obra en Comas",
         "Se requieren 5 ayudantes con experiencia mínima de 1 año. Pago semanal.",
         "Albañilería, acabados", "construccion", "tiempo_completo", "S/1200 - S/1400", 5, "publicaciones/pub_13.jpg"),
    };
    var nuevas = 0;
    foreach (var d in demos)
    {
        if (!await dbs.Publicaciones.AnyAsync(p => p.Titulo == d.Titulo && p.AutorId == d.AutorId))
        {
            var pub = new Publicacion
            {
                AutorId = d.AutorId, Tipo = d.Tipo, Titulo = d.Titulo, Contenido = d.Contenido,
                HabilidadesReq = d.Hab, Categoria = d.Cat, Modalidad = d.Mod,
                PrecioTexto = d.Precio, Vacantes = d.Vac, Imagen = d.Img, Activa = true
            };
            dbs.Publicaciones.Add(pub);
            nuevas++;
        }
    }
    await dbs.SaveChangesAsync();

    // Hilo de comentarios + calificaciones demo (por texto, no duplica).
    async Task AddComentarioAsync(int pubId, string autorId, string texto, int? parentId = null, string? respA = null)
    {
        if (!await dbs.ComentariosHilo.AnyAsync(c => c.PublicacionId == pubId && c.Comentario == texto))
            dbs.ComentariosHilo.Add(new ComentarioPublicacion
            { PublicacionId = pubId, AutorId = autorId, Comentario = texto, Activo = true, ParentId = parentId, RespuestaAUsuarioId = respA });
    }
    async Task AddValoracionAsync(string autorId, string destId, int pubId, int estrellas, string texto)
    {
        if (!await dbs.Valoraciones.AnyAsync(v => v.PublicacionId == pubId && v.AutorId == autorId))
            dbs.Valoraciones.Add(new ComentarioValoracion
            { AutorId = autorId, DestinatarioId = destId, PublicacionId = pubId, Calificacion = estrellas, Comentario = texto, Recomendacion = estrellas >= 4, Activo = true });
    }
    {
        var soldador = await dbs.Publicaciones
            .FirstAsync(p => p.Titulo!.Contains("Soldador") && p.AutorId == emp.Id);
        var gasfitero = await dbs.Publicaciones
            .FirstAsync(p => p.Titulo!.Contains("gasfitero") && p.AutorId == tra.Id);
        var limpieza = await dbs.Publicaciones
            .FirstAsync(p => p.Titulo!.Contains("limpieza") && p.AutorId == emp2.Id);
        var branding = await dbs.Publicaciones
            .FirstAsync(p => p.Titulo!.Contains("branding") && p.AutorId == tra2.Id);

        await AddComentarioAsync(soldador.Id, tra.Id, "Buenas tardes, tengo 6 años soldando MIG y TIG. ¿Siguen buscando?");
        await dbs.SaveChangesAsync();
        var base1 = await dbs.ComentariosHilo
            .FirstAsync(c => c.PublicacionId == soldador.Id && c.Comentario.StartsWith("Buenas tardes"));
        await AddComentarioAsync(soldador.Id, emp.Id, "Sí, escríbanos por interno con su CV para coordinar una prueba.", base1.Id, tra.Id);
        await AddComentarioAsync(soldador.Id, tra2.Id, "¿Aceptan practicantes de soldadura con 1 año de experiencia?");
        await dbs.SaveChangesAsync();
        var base2 = await dbs.ComentariosHilo
            .FirstAsync(c => c.PublicacionId == soldador.Id && c.Comentario.StartsWith("¿Aceptan"));
        await AddComentarioAsync(soldador.Id, emp.Id, "Por ahora buscamos personal con experiencia, pero guardamos tu contacto.", base2.Id, tra2.Id);
        await AddComentarioAsync(gasfitero.Id, emp.Id, "Buen trabajo, ¿hace instalaciones en Lima también?");
        await dbs.SaveChangesAsync();
        var base3 = await dbs.ComentariosHilo
            .FirstAsync(c => c.PublicacionId == gasfitero.Id && c.AutorId == emp.Id);
        await AddComentarioAsync(gasfitero.Id, tra.Id, "Sí, viajo a Lima con coordinación previa de una semana.", base3.Id, emp.Id);
        await AddComentarioAsync(limpieza.Id, tra.Id, "¿El turno mañana es de 8 a 1? Me interesa.");
        await AddComentarioAsync(branding.Id, emp.Id, "Lindo portafolio. ¿Haces también diseño de empaques?");
        await dbs.SaveChangesAsync();

        await AddValoracionAsync(tra.Id, emp.Id, soldador.Id, 5, "Empresa seria, pagan puntual.");
        await AddValoracionAsync(emp.Id, tra.Id, gasfitero.Id, 5, "Trabajo limpio y a tiempo.");
        await AddValoracionAsync(tra2.Id, emp.Id, soldador.Id, 4, "Buena comunicación en la entrevista.");
        await AddValoracionAsync(emp.Id, tra2.Id, branding.Id, 5, "Diseño excelente, cumplió antes del plazo.");
        await AddValoracionAsync(tra.Id, emp2.Id, limpieza.Id, 3, "Todo bien, aunque el pago demoró unos días.");
        await AddValoracionAsync(emp2.Id, tra.Id, gasfitero.Id, 4, "Recomendado para instalaciones.");
        await dbs.SaveChangesAsync();

        // Respuesta demo a una calificación (hija con PublicacionId null).
        var padreVal = await dbs.Valoraciones
            .FirstAsync(v => v.PublicacionId == soldador.Id && v.AutorId == tra.Id && v.ParentId == null);
        if (!await dbs.Valoraciones.AnyAsync(v => v.ParentId == padreVal.Id))
            dbs.Valoraciones.Add(new ComentarioValoracion
            { AutorId = emp.Id, DestinatarioId = tra.Id, PublicacionId = null, Comentario = "Gracias por su comentario, lo esperamos en la prueba.", ParentId = padreVal.Id, RespuestaAUsuarioId = tra.Id, Activo = true });
        await dbs.SaveChangesAsync();

        // Postulaciones demo (pendiente / aceptada / rechazada).
        async Task AddSolicitudAsync(int pubId, string trabId, string estado, string mensaje)
        {
            if (!await dbs.Solicitudes.AnyAsync(s => s.PublicacionId == pubId && s.TrabajadorId == trabId))
                dbs.Solicitudes.Add(new Solicitud
                { PublicacionId = pubId, TrabajadorId = trabId, Estado = estado, Mensaje = mensaje, IncluyeCv = true, Activa = true });
        }
        var maestro = await dbs.Publicaciones
            .FirstAsync(p => p.Titulo!.Contains("maestro de obra") && p.AutorId == emp.Id);
        await AddSolicitudAsync(soldador.Id, tra.Id, EstadosSolicitud.Pendiente, "Postulación con un clic - perfil y experiencia adjuntos.");
        await AddSolicitudAsync(limpieza.Id, tra2.Id, EstadosSolicitud.Aceptada, "Tengo experiencia en limpieza de oficinas, disponibilidad inmediata.");
        await AddSolicitudAsync(maestro.Id, tra.Id, EstadosSolicitud.Rechazada, "Me interesa la obra de Miraflores, tengo 5 años como ayudante.");
        await dbs.SaveChangesAsync();

        // Notificaciones demo (campana).
        async Task AddNotifAsync(string userId, string titulo, string mensaje, string enlace)
        {
            if (!await dbs.Notificaciones.AnyAsync(n => n.UsuarioId == userId && n.Titulo == titulo))
                dbs.Notificaciones.Add(new Notificacion
                { UsuarioId = userId, Titulo = titulo, Mensaje = mensaje, Enlace = enlace });
        }
        await AddNotifAsync(tra2.Id, "Postulación aceptada", "Su postulación ha sido aceptada.", "/Solicitudes/MisPostulaciones");
        await AddNotifAsync(emp.Id, "Nueva postulación recibida", "Tiene una nueva postulación por revisar.", "/Solicitudes/Recibidas");
        await AddNotifAsync(tra.Id, "Nuevo comentario", "Respondieron tu comentario en la publicación de soldador.", "/Feed/Details/" + soldador.Id);
        await dbs.SaveChangesAsync();

        // Denuncia demo pendiente (vista del admin).
        if (!await dbs.Denuncias.AnyAsync())
            dbs.Denuncias.Add(new Denuncia
            { DenuncianteId = tra.Id, ObjetoTipo = "comentario", ObjetoId = base1.Id, Motivo = "spam", Descripcion = "Comentario demo para la cola de moderación.", Estado = "pendiente" });
        await dbs.SaveChangesAsync();

        // Reputación recalculada desde las valoraciones (lo que muestra el Panel).
        foreach (var u in new[] { emp, tra, tra2, emp2 })
        {
            var vals = await dbs.Valoraciones
                .Where(v => v.DestinatarioId == u.Id && v.Activo && v.Calificacion != null)
                .Select(v => v.Calificacion!.Value).ToListAsync();
            var rep = await dbs.Reputaciones.FindAsync(u.Id);
            if (rep is null) { rep = new Reputacion { UsuarioId = u.Id }; dbs.Reputaciones.Add(rep); }
            rep.TotalCalificaciones = vals.Count;
            rep.PromedioEstrellas = vals.Count == 0 ? 0 : vals.Average();
        }
        await dbs.SaveChangesAsync();
        app.Logger.LogInformation("Seed-demo: hilos, estrellas, postulaciones y extras listos.");
    }

    var activas = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
        .ToListAsync(dbs.Publicaciones.Where(p => p.Activa));
    var ok = 0;
    foreach (var pub in activas)
    {
        try { await algoliaSeed.IndexPublicacionAsync(pub); ok++; }
        catch (Exception ex) { app.Logger.LogWarning(ex, "No se indexó {Id}.", pub.Id); }
    }
    app.Logger.LogInformation("Seed-demo listo: {Nuevas} nuevas. Indexadas {Ok}/{Total} en Algolia.", nuevas, ok, activas.Count);
    return;
}
app.Run();
