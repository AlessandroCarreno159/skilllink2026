namespace SkillLink_dotnet.Services.Geo;

/// <summary>Configuración de la integración con OpenStreetMap / Nominatim.</summary>
public sealed class GeoOptions
{
    public const string SectionName = "Geo";

    public bool Enabled { get; set; } = true;

    public string BaseUrl { get; set; } = "https://nominatim.openstreetmap.org";

    /// <summary>Obligatorio según la política de uso de Nominatim.</summary>
    public string UserAgent { get; set; } = "SkillLinkApp/1.0 (sebastianhumpiremenzala1@gmail.com)";

    public int CacheMinutes { get; set; } = 1440;

    public int TimeoutSeconds { get; set; } = 5;
}
