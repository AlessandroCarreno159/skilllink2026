namespace SkillLink_dotnet.Services.Currency;

/// <summary>
/// Configuración de ExchangeRate-API (https://www.exchangerate-api.com).
/// Sin ApiKey el servicio queda inactivo y las vistas muestran solo el precio base.
/// </summary>
public sealed class CurrencyOptions
{
    public const string SectionName = "Currency";

    public bool Enabled { get; set; } = true;
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://v6.exchangerate-api.com/v6";
    public int CacheHours { get; set; } = 12;
    public decimal FallbackPenToUsd { get; set; } = 0.29m;
    public int TimeoutSeconds { get; set; } = 8;

    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(ApiKey);
}
