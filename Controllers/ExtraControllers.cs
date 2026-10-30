using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SkillLink_dotnet.Data;
using SkillLink_dotnet.Models;
using SkillLink_dotnet.Services;
using SkillLink_dotnet.Services.Ml;
using SkillLink_dotnet.ViewModels;

namespace SkillLink_dotnet.Controllers;

[Authorize]
public class DashboardController(
    ApplicationDbContext db,
    UserManager<ApplicationUser> users,
    ReputacionService reputacion) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var yo = (await users.GetUserAsync(User))!;
        if (yo.Rol == Roles.Admin) return RedirectToAction("Usuarios", "Admin");
        var (prom, total) = await reputacion.ObtenerAsync(yo.Id);
        ViewBag.RepPromedio = prom; ViewBag.RepTotal = total; ViewBag.Estado = yo.EstadoCuenta;
        ViewBag.Pubs = await db.Publicaciones.Where(p => p.AutorId == yo.Id && p.Activa)
            .OrderByDescending(p => p.FechaCreacion).Take(10).ToListAsync();
        if (yo.Rol == Roles.Trabajador)
        {
            var lista = await db.Solicitudes
                .Where(s => s.TrabajadorId == yo.Id && s.Activa)
                .OrderByDescending(s => s.FechaPostulacion)
                .Select(s => new { S = s, P = s.Publicacion!, Razon = s.Publicacion!.Autor!.Empresa!.RazonSocial })
                .ToListAsync();
            ViewBag.Postulaciones = lista.Select(x => (x.S, x.P, x.Razon)).ToList();
        }
        return View();
    }
}

[Authorize(Roles = Roles.Admin)]
public class AdminController(
    ApplicationDbContext db,
    UserManager<ApplicationUser> users,
    ImageUploadService uploads,
    SkillLink.Services.AlgoliaService algolia,
    SpamClasificadorService spam,
    ModeracionService moderacion) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Usuarios(string estado = "pendiente")
    {
        var q = db.Users
            .Include(u => u.Perfil)
            .Include(u => u.Empresa)
            .Where(u => u.Rol != Roles.Admin);
        if (estado != "todos") q = q.Where(u => u.EstadoCuenta == estado);
        ViewBag.Estado = estado;
        return View(await q.OrderByDescending(u => u.FechaRegistro).ToListAsync());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Gestionar(string id, string accion)
    {
        var map = new Dictionary<string, string>
        {
            ["aprobar"] = EstadosCuenta.Aprobado, ["rechazar"] = EstadosCuenta.Rechazado,
            ["suspender"] = EstadosCuenta.Suspendido, ["banear"] = EstadosCuenta.Baneado
        };
        if (!map.TryGetValue(accion, out var estado)) { TempData["Error"] = "Acción no válida."; return RedirectToAction(nameof(Usuarios)); }
        var u = await db.Users.FindAsync(id);
        if (u is null) return NotFound();
        u.EstadoCuenta = estado;
        var adminG = await users.GetUserAsync(User);
        db.HistorialModeracion.Add(new HistorialModeracion { UsuarioId = id, AdminId = adminG?.Id, Accion = accion });
        if (estado == EstadosCuenta.Baneado)
        {
            // El baneo bloquea el DNI: sin este registro el usuario podía re-registrarse.
            if (!await db.BaneosDni.AnyAsync(b => b.Dni == u.Dni))
                db.BaneosDni.Add(new BaneoDni { Dni = u.Dni, Motivo = "Baneo por moderación", BaneadoPor = adminG?.Id });
        }
        else if (estado == EstadosCuenta.Aprobado)
        {
            // Aprobar levanta el baneo del DNI si existía.
            var baneo = await db.BaneosDni.FirstOrDefaultAsync(b => b.Dni == u.Dni);
            if (baneo is not null) db.BaneosDni.Remove(baneo);
        }
        await db.SaveChangesAsync();
        TempData["Success"] = $"Cuenta {estado}.";
        return RedirectToAction(nameof(Usuarios));
    }

    [HttpGet]
    public async Task<IActionResult> Denuncias(bool soloRiesgo = false)
    {
        var denuncias = await db.Denuncias.OrderByDescending(d => d.Fecha).ToListAsync();
        var pubIds = denuncias.Where(d => d.ObjetoTipo == "publicacion").Select(d => d.ObjetoId).Distinct().ToList();
        var comIds = denuncias.Where(d => d.ObjetoTipo == "comentario").Select(d => d.ObjetoId).Distinct().ToList();
        var pubs = await db.Publicaciones.Where(p => pubIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Titulo, p.Contenido }).ToDictionaryAsync(x => x.Id);
        var coms = await db.ComentariosHilo.Where(c => comIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Comentario }).ToDictionaryAsync(x => x.Id);

        var lista = denuncias.Select(d =>
        {
            var texto = d.ObjetoTipo switch
            {
                "publicacion" when pubs.TryGetValue(d.ObjetoId, out var p) => $"{p.Titulo} {p.Contenido}",
                "comentario" when coms.TryGetValue(d.ObjetoId, out var c) => c.Comentario,
                _ => d.Descripcion ?? string.Empty
            };
            var extracto = string.IsNullOrWhiteSpace(texto) ? "(sin texto disponible)" : texto.Trim();
            if (extracto.Length > 120) extracto = extracto[..120] + "…";
            var riesgo = string.IsNullOrWhiteSpace(texto)
                ? new RiesgoContenido(0, 0, 0, 0, "Normal")
                : spam.Predecir(texto);
            return new DenunciaRiesgoVm(d, extracto, riesgo);
        }).ToList();

        if (soloRiesgo) lista = lista.Where(x => x.Riesgoso).ToList();
        lista = lista.OrderByDescending(x => x.Denuncia.Estado == "pendiente")
            .ThenByDescending(x => x.Probabilidad).ToList();
        ViewBag.SoloRiesgo = soloRiesgo;
        ViewBag.Modo = spam.Modo;
        return View(lista);
    }

    // Resolver una denuncia: resuelta (spam), basura o desestimada.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ResolverDenuncia(int id, string accion)
    {
        var map = new Dictionary<string, string>
        {
            ["resolver"] = "resuelta",
            ["desestimar"] = "desestimada",
            ["basura"] = "basura",
            ["peligroso"] = "peligroso"
        };
        if (!map.TryGetValue(accion, out var estado))
        { TempData["Error"] = "Acción no válida."; return RedirectToAction(nameof(Denuncias)); }
        var d = await db.Denuncias.FindAsync(id);
        if (d is null) { TempData["Error"] = "Denuncia no encontrada."; return RedirectToAction(nameof(Denuncias)); }
        d.Estado = estado;
        await moderacion.AplicarResolucionAsync(d, estado);
        var admin = await users.GetUserAsync(User);
        db.HistorialModeracion.Add(new HistorialModeracion
        {
            UsuarioId = d.DenuncianteId == DenunciasSistema.Id ? (admin?.Id ?? d.DenuncianteId) : d.DenuncianteId,
            AdminId = admin?.Id,
            Accion = "denuncia_" + accion, ObjetoTipo = d.ObjetoTipo, ObjetoId = d.ObjetoId
        });
        await db.SaveChangesAsync();
        TempData["Success"] = estado switch
        {
            "basura" => "Marcada como basura. El contenido quedó oculto.",
            "peligroso" => "Marcada como peligrosa. El contenido quedó oculto.",
            "resuelta" => "Confirmada como spam. El contenido quedó oculto.",
            _ => $"Denuncia {estado}."
        };
        return RedirectToAction(nameof(Denuncias));
    }

    [HttpGet]
    public async Task<IActionResult> Publicaciones(bool soloRiesgo = false, int pagina = 1)
    {
        const int porPagina = 20;

        var candidatos = await db.Publicaciones.AsNoTracking()
            .Select(p => new
            {
                p.Id,
                p.Titulo,
                p.Contenido,
                p.HabilidadesReq,
                p.FechaCreacion
            })
            .ToListAsync();

        var riesgos = candidatos.ToDictionary(
            c => c.Id,
            c => spam.Predecir($"{c.Titulo} {c.Contenido} {c.HabilidadesReq}"));

        var totalRiesgo = riesgos.Values.Count(r => r.Probabilidad >= SpamRiesgo.Umbral);
        var filtrados = soloRiesgo
            ? candidatos.Where(c => riesgos[c.Id].Probabilidad >= SpamRiesgo.Umbral).ToList()
            : candidatos;

        var total = filtrados.Count;
        var totalPaginas = Math.Max(1, (int)Math.Ceiling(total / (double)porPagina));
        pagina = Math.Clamp(pagina, 1, totalPaginas);

        var idsPagina = filtrados
            .OrderByDescending(c => riesgos[c.Id].Probabilidad)
            .ThenByDescending(c => c.FechaCreacion)
            .ThenByDescending(c => c.Id)
            .Skip((pagina - 1) * porPagina)
            .Take(porPagina)
            .Select(c => c.Id)
            .ToList();

        var pubs = await db.Publicaciones.AsNoTracking()
            .Include(p => p.Autor).ThenInclude(a => a!.Perfil)
            .Include(p => p.Autor).ThenInclude(a => a!.Empresa)
            .Where(p => idsPagina.Contains(p.Id))
            .ToListAsync();
        var pubsPorId = pubs.ToDictionary(p => p.Id);
        var lista = idsPagina
            .Where(pubsPorId.ContainsKey)
            .Select(id => new PublicacionRiesgoVm(pubsPorId[id], riesgos[id]))
            .ToList();

        ViewBag.Pagina = pagina;
        ViewBag.TotalPaginas = totalPaginas;
        ViewBag.Total = total;
        ViewBag.TotalGeneral = candidatos.Count;
        ViewBag.TotalRiesgo = totalRiesgo;
        ViewBag.SoloRiesgo = soloRiesgo;
        ViewBag.Modo = spam.Modo;
        return View(lista);
    }

    // Solo el admin edita nombres (NombreCompleto/RazonSocial).
    [HttpGet]
    public async Task<IActionResult> EditarNombre(string id)
    {
        var u = await db.Users.Include(x => x.Perfil).Include(x => x.Empresa)
            .FirstOrDefaultAsync(x => x.Id == id && x.Rol != Roles.Admin);
        if (u is null) return NotFound();
        return View(u);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditarNombre(string id, string nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre))
        { TempData["Error"] = "El nombre no puede estar vacío."; return RedirectToAction(nameof(EditarNombre), new { id }); }
        var u = await db.Users.Include(x => x.Perfil).Include(x => x.Empresa)
            .FirstOrDefaultAsync(x => x.Id == id && x.Rol != Roles.Admin);
        if (u is null) return NotFound();
        nombre = nombre.Trim();
        if (u.Rol == Roles.Trabajador && u.Perfil is not null) u.Perfil.NombreCompleto = nombre;
        else if (u.Rol == Roles.Empresa && u.Empresa is not null) u.Empresa.RazonSocial = nombre;
        else { TempData["Error"] = "El usuario aún no tiene perfil."; return RedirectToAction(nameof(EditarNombre), new { id }); }
        var admin = await users.GetUserAsync(User);
        db.HistorialModeracion.Add(new HistorialModeracion
        { UsuarioId = id, AdminId = admin?.Id, Accion = "editar_nombre" });
        await db.SaveChangesAsync();
        TempData["Success"] = "Nombre actualizado.";
        return RedirectToAction(nameof(Usuarios), new { estado = "todos" });
    }

    // Borrado físico: elimina la publicación con sus solicitudes y comentarios (cascada),
    // registra la moderación y borra la imagen del disco para no dejar huérfanos.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EliminarPublicacion(int id, string? returnUrl = null)
    {
        var p = await db.Publicaciones.FindAsync(id);
        if (p is null) { TempData["Error"] = "Publicación no encontrada."; return RedirectToAction(nameof(Publicaciones)); }
        var admin = await users.GetUserAsync(User);
        db.HistorialModeracion.Add(new HistorialModeracion
        {
            UsuarioId = p.AutorId, AdminId = admin?.Id,
            Accion = "eliminar_publicacion", ObjetoTipo = "publicacion", ObjetoId = id
        });
        db.Publicaciones.Remove(p);
        await db.SaveChangesAsync();
        uploads.Borrar(p.Imagen);
        await algolia.DeletePublicacionAsync(id);
        TempData["Success"] = "Publicación eliminada definitivamente.";
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)) return Redirect(returnUrl);
        return RedirectToAction(nameof(Publicaciones));
    }
}

[Authorize]
public class NotificationsController(ApplicationDbContext db, UserManager<ApplicationUser> users) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var yo = (await users.GetUserAsync(User))!;
        return View(await db.Notificaciones.Where(n => n.UsuarioId == yo.Id)
            .OrderByDescending(n => n.Fecha).ToListAsync());
    }

    [HttpGet]
    public async Task<IActionResult> NoLeidas()
    {
        var yo = await users.GetUserAsync(User);
        if (yo is null) return Json(new { total = 0 });
        var total = await db.Notificaciones.CountAsync(n => n.UsuarioId == yo.Id && !n.Leida);
        return Json(new { total });
    }
}
