using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace SkillLink_dotnet.Services.Geo;

/// <summary>
/// Geocodificación inversa contra el endpoint público de Nominatim.
/// Respeta la política de uso: User-Agent identificable, caché por coordenada
/// redondeada y un máximo aproximado de una petición por segundo.
/// </summary>
public sealed class GeoService
{
    private static readonly SemaphoreSlim PuertaNominatim = new(1, 1);
    private static readonly TimeSpan IntervaloMinimo = TimeSpan.FromSeconds(1);
    private static DateTime _ultimaPeticionUtc = DateTime.MinValue;

    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly ILogger<GeoService> _logger;
    private readonly GeoOptions _options;

    public GeoService(
        HttpClient http,
        IOptions<GeoOptions> options,
        IMemoryCache cache,
        ILogger<GeoService> logger)
    {
        _http = http;
        _cache = cache;
        _logger = logger;
        _options = options.Value;

        if (_http.BaseAddress is null && Uri.TryCreate(_options.BaseUrl, UriKind.Absolute, out var baseUri))
            _http.BaseAddress = baseUri;

        _http.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds > 0 ? _options.TimeoutSeconds : 5);
    }

    public async Task<string?> ReverseCityAsync(double lat, double lon, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled) return null;
        if (!CoordenadaValida(lat, lon)) return null;

        var latRedondeada = Math.Round(lat, 3);
        var lonRedondeada = Math.Round(lon, 3);
        var cacheKey = string.Create(
            CultureInfo.InvariantCulture,
            $"geo:reverse:{latRedondeada:F3}:{lonRedondeada:F3}:es");

        if (_cache.TryGetValue(cacheKey, out CiudadCache? cacheado) && cacheado is not null)
            return cacheado.Ciudad;

        try
        {
            await EsperarTurnoAsync(cancellationToken);

            using var request = new HttpRequestMessage(HttpMethod.Get, ConstruirUrl(latRedondeada, lonRedondeada));
            AplicarUserAgent(request);

            using var response = await _http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Nominatim respondió {StatusCode} para {Lat},{Lon}.",
                    (int)response.StatusCode, latRedondeada, lonRedondeada);
                return Cachear(cacheKey, null);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var documento = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var departamento = ExtraerDepartamento(documento.RootElement);

            if (departamento is null)
            {
                _logger.LogInformation(
                    "Nominatim no devolvió un departamento peruano reconocible para {Lat},{Lon}.",
                    latRedondeada, lonRedondeada);
            }

            return Cachear(cacheKey, departamento);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "No se pudo obtener la ciudad desde Nominatim para {Lat},{Lon}.",
                latRedondeada, lonRedondeada);
            return null;
        }
    }

    private static bool CoordenadaValida(double lat, double lon)
        => !double.IsNaN(lat) && !double.IsNaN(lon)
           && !double.IsInfinity(lat) && !double.IsInfinity(lon)
           && lat is >= -90 and <= 90
           && lon is >= -180 and <= 180;

    private string ConstruirUrl(double lat, double lon)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? (_http.BaseAddress?.ToString() ?? "https://nominatim.openstreetmap.org")
            : _options.BaseUrl;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{baseUrl.TrimEnd('/')}/reverse?format=jsonv2&lat={lat:F6}&lon={lon:F6}&zoom=10&accept-language=es");
    }

    private void AplicarUserAgent(HttpRequestMessage request)
    {
        if (string.IsNullOrWhiteSpace(_options.UserAgent)) return;
        if (request.Headers.UserAgent.Count > 0 || _http.DefaultRequestHeaders.UserAgent.Count > 0) return;

        if (!request.Headers.UserAgent.TryParseAdd(_options.UserAgent))
            request.Headers.TryAddWithoutValidation("User-Agent", _options.UserAgent);
    }

    private static async Task EsperarTurnoAsync(CancellationToken cancellationToken)
    {
        await PuertaNominatim.WaitAsync(cancellationToken);
        try
        {
            var espera = IntervaloMinimo - (DateTime.UtcNow - _ultimaPeticionUtc);
            if (espera > TimeSpan.Zero)
                await Task.Delay(espera, cancellationToken);

            _ultimaPeticionUtc = DateTime.UtcNow;
        }
        finally
        {
            PuertaNominatim.Release();
        }
    }

    private string? Cachear(string key, string? departamento)
    {
        var minutos = _options.CacheMinutes > 0 ? _options.CacheMinutes : 1440;
        _cache.Set(key, new CiudadCache(departamento), TimeSpan.FromMinutes(minutos));
        return departamento;
    }

    private static string? ExtraerDepartamento(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;

        if (root.TryGetProperty("address", out var address) && address.ValueKind == JsonValueKind.Object)
        {
            string[] campos =
            {
                "state", "region", "province", "state_district",
                "county", "municipality", "city", "town", "village", "name",
            };

            foreach (var campo in campos)
            {
                if (!address.TryGetProperty(campo, out var valor) || valor.ValueKind != JsonValueKind.String)
                    continue;

                var departamento = Catalogos.NormalizarDepartamento(valor.GetString());
                if (departamento is not null) return departamento;
            }
        }

        if (root.TryGetProperty("display_name", out var display) && display.ValueKind == JsonValueKind.String)
        {
            var partes = display.GetString()?.Split(
                ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                ?? Array.Empty<string>();

            foreach (var parte in partes)
            {
                var departamento = Catalogos.NormalizarDepartamento(parte);
                if (departamento is not null) return departamento;
            }
        }

        return null;
    }

    private sealed record CiudadCache(string? Ciudad);
}
