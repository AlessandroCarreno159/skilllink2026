using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SkillLink_dotnet.Models;

namespace SkillLink_dotnet.Services.Currency;

/// <summary>
/// Obtiene el tipo de cambio PEN/USD desde ExchangeRate-API y lo cachea.
/// Un fallo de red o de cuota no rompe la vista: se usa la tasa de respaldo
/// o se devuelve null para omitir el precio convertido.
/// </summary>
public sealed class ExchangeRateService
{
    private const string CacheKey = "currency:pen-usd";
    private static readonly TimeSpan FailureTtl = TimeSpan.FromMinutes(5);
    // El cliente tipado es transitorio: el lock estático evita que varias
    // peticiones simultáneas consulten la API cuando la caché está vacía.
    private static readonly SemaphoreSlim Lock = new(1, 1);

    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly CurrencyOptions _options;
    private readonly ILogger<ExchangeRateService> _logger;

    public ExchangeRateService(
        HttpClient http,
        IMemoryCache cache,
        IOptions<CurrencyOptions> options,
        ILogger<ExchangeRateService> logger)
    {
        _http = http;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured => _options.IsConfigured;

    /// <summary>Tasa USD por 1 PEN. Devuelve null si no hay tasa disponible.</summary>
    public async Task<decimal?> GetPenToUsdAsync(CancellationToken ct = default)
    {
        if (!_options.IsConfigured) return null;

        // 0 = intento fallido reciente; se cachea para no golpear la API en cada request.
        if (_cache.TryGetValue(CacheKey, out decimal cached))
            return cached > 0 ? cached : null;

        await Lock.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(CacheKey, out cached))
                return cached > 0 ? cached : null;

            var rate = await FetchAsync(ct);
            var ttl = rate is > 0
                ? TimeSpan.FromHours(Math.Max(1, _options.CacheHours))
                : FailureTtl;
            _cache.Set(CacheKey, rate, ttl);
            return rate > 0 ? rate : null;
        }
        finally
        {
            Lock.Release();
        }
    }

    /// <summary>Convierte un monto entre PEN y USD. Null si no hay tasa.</summary>
    public async Task<decimal?> ConvertAsync(decimal amount, string from, string to, CancellationToken ct = default)
    {
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) return amount;

        var penToUsd = await GetPenToUsdAsync(ct);
        if (penToUsd is not > 0) return null;

        if (EsMoneda(from, Monedas.Pen) && EsMoneda(to, Monedas.Usd))
            return amount * penToUsd.Value;
        if (EsMoneda(from, Monedas.Usd) && EsMoneda(to, Monedas.Pen))
            return amount / penToUsd.Value;

        return null;
    }

    private async Task<decimal> FetchAsync(CancellationToken ct)
    {
        try
        {
            var url = $"{_options.BaseUrl.TrimEnd('/')}/{_options.ApiKey}/latest/PEN";
            using var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("ExchangeRate-API respondió HTTP {Status}. Se usa la tasa de respaldo.", (int)response.StatusCode);
                return Fallback();
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;

            var result = root.TryGetProperty("result", out var r) ? r.GetString() : null;
            if (!string.Equals(result, "success", StringComparison.OrdinalIgnoreCase))
            {
                var error = root.TryGetProperty("error-type", out var e) ? e.GetString() : "desconocido";
                _logger.LogWarning("ExchangeRate-API devolvió error '{Error}'. Se usa la tasa de respaldo.", error);
                return Fallback();
            }

            if (root.TryGetProperty("conversion_rates", out var rates)
                && rates.TryGetProperty("USD", out var usd)
                && usd.TryGetDecimal(out var value)
                && value > 0)
            {
                return value;
            }

            _logger.LogWarning("ExchangeRate-API no devolvió conversion_rates.USD. Se usa la tasa de respaldo.");
            return Fallback();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo consultar ExchangeRate-API. Se usa la tasa de respaldo.");
            return Fallback();
        }
    }

    private decimal Fallback() => _options.FallbackPenToUsd > 0 ? _options.FallbackPenToUsd : 0m;

    private static bool EsMoneda(string value, string expected)
        => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
}
