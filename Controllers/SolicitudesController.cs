using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SkillLink_dotnet.Data;
using SkillLink_dotnet.Filters;
using SkillLink_dotnet.Models;
using SkillLink_dotnet.Services;

namespace SkillLink_dotnet.Controllers;

/// <summary>
/// Solicitudes = postulaciones (applications.py). Solo usuarios autenticados.
/// </summary>
[Authorize]
public class SolicitudesController(
    ApplicationDbContext db,
    UserManager<ApplicationUser> users,
    NotificacionService notificaciones,
    RealtimePublisher realtime) : Controller
{
    private Task<ApplicationUser?> ActualAsync() => users.GetUserAsync(User);

    // POST /Solicitudes/Postular/<pubId> — trabajador con cuenta activa
    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.Trabajador)]
    [CuentaActiva]
    public async Task<IActionResult> Postular(int publicacionId, string? mensaje)
    {
        var pub = await db.Publicaciones.FirstOrDefaultAsync(p =>
            p.Id == publicacionId && p.Tipo == TiposPublicacion.Oferta && p.Activa);
        if (pub is null) { TempData["Warning"] = "Oferta no disponible."; return RedirectToAction("Index", "Feed"); }

        var yo = (await ActualAsync())!;
        var existe = await db.Solicitudes.FirstOrDefaultAsync(s =>
            s.PublicacionId == publicacionId && s.TrabajadorId == yo.Id);
        if (existe is not null)
        {
            if (existe.Activa) { TempData["Info"] = "Ya postuló a esta oferta."; return RedirectToAction("Details", "Feed", new { id = publicacionId }); }
            existe.Activa = true;
            existe.Estado = EstadosSolicitud.Pendiente;
            existe.Mensaje = string.IsNullOrWhiteSpace(mensaje) ? existe.Mensaje : mensaje!;
            existe.FechaPostulacion = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await notificaciones.CrearAsync(pub.AutorId, "Nueva postulación recibida",
                "Tiene una nueva postulación por revisar.", "/Solicitudes/Recibidas");
            await PublicarPostulacionAsync(pub, yo.Id, existe.Id, existe.Mensaje);
            TempData["Success"] = "Postulación enviada correctamente.";
            return RedirectToAction("Details", "Feed", new { id = publicacionId });
        }

        var nueva = new Solicitud
        {
            PublicacionId = publicacionId,
            TrabajadorId = yo.Id,
            Mensaje = string.IsNullOrWhiteSpace(mensaje)
                ? "Postulación con un clic - perfil y experiencia adjuntos." : mensaje!,
            IncluyeCv = true,
            Estado = EstadosSolicitud.Pendiente
        };
        db.Solicitudes.Add(nueva);
        await db.SaveChangesAsync();
        await notificaciones.CrearAsync(pub.AutorId, "Nueva postulación recibida",
            "Tiene una nueva postulación por revisar.", "/Solicitudes/Recibidas");
        await PublicarPostulacionAsync(pub, yo.Id, nueva.Id, nueva.Mensaje);
        TempData["Success"] = "Postulación enviada correctamente.";
        return RedirectToAction("Details", "Feed", new { id = publicacionId });
    }

    private async Task PublicarPostulacionAsync(
        Publicacion pub, string trabajadorId, int solicitudId, string? mensaje)
    {
        var trabajadorNombre = await db.Perfiles
            .Where(p => p.UsuarioId == trabajadorId)
            .Select(p => p.NombreCompleto)
            .FirstOrDefaultAsync();

        await realtime.PublishAsync("application:new", pub.AutorId, new
        {
            solicitudId,
            publicacionId = pub.Id,
            publicacionTitulo = pub.Titulo ?? $"Oferta #{pub.Id}",
            trabajadorId,
            trabajadorNombre = trabajadorNombre ?? "Un postulante",
            mensaje
        });
    }

    [HttpGet]
    [Authorize(Roles = Roles.Trabajador)]
    public async Task<IActionResult> MisPostulaciones()
    {
        var yo = (await ActualAsync())!;
        var lista = await db.Solicitudes
            .Where(s => s.TrabajadorId == yo.Id && s.Activa)
            .OrderByDescending(s => s.FechaPostulacion)
            .Select(s => new { S = s, P = s.Publicacion!, Razon = s.Publicacion!.Autor!.Empresa!.RazonSocial })
            .ToListAsync();
        return View(lista.Select(x => (x.S, x.P, x.Razon)).ToList());
    }

    [HttpGet]
    [Authorize(Roles = Roles.Empresa)]
    [CuentaActiva]
    public async Task<IActionResult> Recibidas()
    {
        var yo = (await ActualAsync())!;
        var lista = await db.Solicitudes
            .Include(s => s.Publicacion)
            .Include(s => s.Trabajador).ThenInclude(t => t!.Perfil)
            .Where(s => s.Publicacion!.AutorId == yo.Id && s.Activa)
            .OrderByDescending(s => s.FechaPostulacion).ToListAsync();
        return View(lista);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.Empresa)]
    [CuentaActiva]
    public async Task<IActionResult> Gestionar(int id, string accion)
    {
        if (accion is not ("aceptar" or "rechazar"))
        { TempData["Error"] = "Acción no válida."; return RedirectToAction(nameof(Recibidas)); }
        var yo = (await ActualAsync())!;
        var s = await db.Solicitudes
            .FirstOrDefaultAsync(x => x.Id == id && x.Publicacion!.AutorId == yo.Id);
        if (s is null) { TempData["Warning"] = "Postulación no encontrada."; return RedirectToAction(nameof(Recibidas)); }

        s.Estado = accion == "aceptar" ? EstadosSolicitud.Aceptada : EstadosSolicitud.Rechazada;
        await db.SaveChangesAsync();
        await notificaciones.CrearAsync(s.TrabajadorId, $"Postulación {s.Estado}",
            $"Su postulación ha sido {s.Estado}.", "/Solicitudes/MisPostulaciones");
        await realtime.PublishAsync("application:status", s.TrabajadorId, new
        {
            solicitudId = s.Id,
            publicacionId = s.PublicacionId,
            estado = s.Estado
        });
        TempData["Success"] = $"Postulación {s.Estado}.";
        return RedirectToAction(nameof(Recibidas));
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.Trabajador)]
    public async Task<IActionResult> Eliminar(int id)
    {
        var yo = (await ActualAsync())!;
        var s = await db.Solicitudes.FirstOrDefaultAsync(x =>
            x.Id == id && x.TrabajadorId == yo.Id && x.Activa);
        if (s is null) { TempData["Error"] = "No puede eliminar esta postulación."; return RedirectToAction("Index", "Dashboard"); }
        s.Activa = false;
        await db.SaveChangesAsync();
        TempData["Success"] = "Postulación eliminada.";
        return RedirectToAction("Index", "Dashboard");
    }
}
