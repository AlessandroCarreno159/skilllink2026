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
    IConfiguration config,
    IHttpClientFactory http,
    ILogger<NotificacionService> logger)
{
    public async Task CrearAsync(string usuarioId, string titulo, string mensaje, string? enlace = null)
    {
        db.Notificaciones.Add(new Models.Notificacion
        { UsuarioId = usuarioId, Titulo = titulo, Mensaje = mensaje, Enlace = enlace });
        await db.SaveChangesAsync();
        // Tiempo real vía PieSocket (un canal, el cliente filtra por usuario).
        // Si no está configurado o falla, la campana sigue por polling.
        var key = config["PieSocket:ApiKey"];
        var secret = config["PieSocket:Secret"];
        var channel = config["PieSocket:ChannelId"];
        var endpoint = config["PieSocket:Endpoint"] ?? "wss://free.nyc1.piesocket.com";
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(channel))
            return;
        try
        {
            // Misma API v3 que el WS del navegador: POST al clúster con `channel`.
            var baseUrl = endpoint.Replace("wss://", "https://").Replace("ws://", "http://").TrimEnd('/');
            var client = http.CreateClient();
            using var resp = await client.PostAsJsonAsync(baseUrl + "/api/publish",
                new
                {
                    key,
                    secret,
                    channel,
                    message = new { usuarioId, titulo, mensaje, enlace }
                });
            resp.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "PieSocket: no se pudo publicar la notificación.");
        }
    }

    public Task<int> NoLeidasAsync(string usuarioId)
        => db.Notificaciones.CountAsync(n => n.UsuarioId == usuarioId && !n.Leida);
}
