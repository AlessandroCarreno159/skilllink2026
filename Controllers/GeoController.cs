using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SkillLink_dotnet.Services.Geo;

namespace SkillLink_dotnet.Controllers;

/// <summary>Ubicación aproximada del visitante (solo departamento, nunca coordenadas exactas).</summary>
public sealed class GeoController(GeoService geo, ILogger<GeoController> logger) : Controller
{
    private static readonly ConcurrentDictionary<string, DateTime> UltimaSolicitud = new();
    private static readonly object ThrottleLock = new();
    private static readonly TimeSpan IntervaloPorIp = TimeSpan.FromSeconds(1);

    [HttpGet("/geo/ciudad")]
    [AllowAnonymous]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Ciudad(
        [FromQuery] double? lat,
        [FromQuery] double? lon,
        CancellationToken cancellationToken)
    {
        if (lat is not double latitud || lon is not double longitud ||
            double.IsNaN(latitud) || double.IsNaN(longitud) ||
            double.IsInfinity(latitud) || double.IsInfinity(longitud) ||
            latitud is < -90 or > 90 || longitud is < -180 or > 180)
        {
            return BadRequest(new { ciudad = (string?)null });
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "desconocida";
        var ahora = DateTime.UtcNow;

        lock (ThrottleLock)
        {
            if (UltimaSolicitud.TryGetValue(ip, out var anterior) && ahora - anterior < IntervaloPorIp)
                return StatusCode(StatusCodes.Status429TooManyRequests, new { ciudad = (string?)null });

            UltimaSolicitud[ip] = ahora;

            if (UltimaSolicitud.Count > 5000)
            {
                var limite = ahora.AddMinutes(-10);
                foreach (var entrada in UltimaSolicitud)
                {
                    if (entrada.Value < limite) UltimaSolicitud.TryRemove(entrada.Key, out _);
                }
            }
        }

        try
        {
            var ciudad = await geo.ReverseCityAsync(latitud, longitud, cancellationToken);
            return Ok(new { ciudad });
        }
        catch (OperationCanceledException)
        {
            return Ok(new { ciudad = (string?)null });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error inesperado al resolver el departamento para {Lat},{Lon}.", latitud, longitud);
            return Ok(new { ciudad = (string?)null });
        }
    }
}
