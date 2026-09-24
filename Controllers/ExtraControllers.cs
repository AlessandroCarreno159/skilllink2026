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
        return View();
    }
}

[Authorize(Roles = Roles.Admin)]
public class AdminController(
    ApplicationDbContext db,
    UserManager<ApplicationUser> users,
    ImageUploadService uploads) : Controller
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
        db.HistorialModeracion.Add(new HistorialModeracion { UsuarioId = id, Accion = accion });
        await db.SaveChangesAsync();
        TempData["Success"] = $"Cuenta {estado}.";
        return RedirectToAction(nameof(Usuarios));
    }

    [HttpGet]
    public async Task<IActionResult> Denuncias()
        => View(await db.Denuncias.OrderByDescending(d => d.Fecha).ToListAsync());

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
