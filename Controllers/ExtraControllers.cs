using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SkillLink_dotnet.Data;
using SkillLink_dotnet.Models;
using SkillLink_dotnet.Services;

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
    SkillLink.Services.AlgoliaService algolia) : Controller
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
    public async Task<IActionResult> Denuncias()
        => View(await db.Denuncias.OrderByDescending(d => d.Fecha).ToListAsync());

    // Resolver una denuncia: resuelta (se tomó acción) o desestimada.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ResolverDenuncia(int id, string accion)
    {
        var map = new Dictionary<string, string>
        {
            ["resolver"] = "resuelta", ["desestimar"] = "desestimada"
        };
        if (!map.TryGetValue(accion, out var estado))
        { TempData["Error"] = "Acción no válida."; return RedirectToAction(nameof(Denuncias)); }
        var d = await db.Denuncias.FindAsync(id);
        if (d is null) { TempData["Error"] = "Denuncia no encontrada."; return RedirectToAction(nameof(Denuncias)); }
        d.Estado = estado;
        var admin = await users.GetUserAsync(User);
        db.HistorialModeracion.Add(new HistorialModeracion
        {
            UsuarioId = d.DenuncianteId, AdminId = admin?.Id,
            Accion = "denuncia_" + accion, ObjetoTipo = d.ObjetoTipo, ObjetoId = d.ObjetoId
        });
        await db.SaveChangesAsync();
        TempData["Success"] = $"Denuncia {estado}.";
        return RedirectToAction(nameof(Denuncias));
    }

    [HttpGet]
    public async Task<IActionResult> Publicaciones()
        => View(await db.Publicaciones
            .Include(p => p.Autor).ThenInclude(a => a!.Perfil)
            .Include(p => p.Autor).ThenInclude(a => a!.Empresa)
            .OrderByDescending(p => p.FechaCreacion).Take(100).ToListAsync());

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
