using Microsoft.EntityFrameworkCore;
using SkillLink_dotnet.Data;

namespace SkillLink_dotnet.Services;

public sealed class ReputacionService(ApplicationDbContext db)
{
    public async Task<(double Promedio, int Total)> ObtenerAsync(string usuarioId)
    {
        var r = await db.Reputaciones.FindAsync(usuarioId);
        return r is null ? (0, 0) : (r.PromedioEstrellas, r.TotalCalificaciones);
    }

    public async Task RecalcularAsync(string usuarioId)
    {
        var vals = await db.Valoraciones
            .Where(v => v.DestinatarioId == usuarioId && v.Activo && v.Calificacion != null)
            .Select(v => v.Calificacion!.Value).ToListAsync();
        var rep = await db.Reputaciones.FindAsync(usuarioId);
        if (rep is null) { rep = new Models.Reputacion { UsuarioId = usuarioId }; db.Reputaciones.Add(rep); }
        rep.TotalCalificaciones = vals.Count;
        rep.PromedioEstrellas = vals.Count == 0 ? 0 : vals.Average();
        await db.SaveChangesAsync();
    }
}

public sealed class NotificacionService(
    ApplicationDbContext db,
    RealtimePublisher realtime,
    ILogger<NotificacionService> logger)
{
    public async Task CrearAsync(string usuarioId, string titulo, string mensaje, string? enlace = null)
    {
        db.Notificaciones.Add(new Models.Notificacion
        { UsuarioId = usuarioId, Titulo = titulo, Mensaje = mensaje, Enlace = enlace });
        await db.SaveChangesAsync();

        // Aviso en tiempo real: el gateway Node escucha este evento en Redis
        // y lo emite a la sala privada user:<usuarioId>. Si Redis falla,
        // la campana sigue funcionando por polling (/Notifications/NoLeidas).
        try
        {
            await realtime.PublishAsync("notification:new", usuarioId,
                new { titulo, mensaje, enlace });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo emitir la notificación en tiempo real.");
        }
    }

    public Task<int> NoLeidasAsync(string usuarioId)
        => db.Notificaciones.CountAsync(n => n.UsuarioId == usuarioId && !n.Leida);
}
