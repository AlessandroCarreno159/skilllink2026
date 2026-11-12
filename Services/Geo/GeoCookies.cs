namespace SkillLink_dotnet.Services.Geo;

/// <summary>Nombres y lectura segura de las cookies de ubicación del visitante.</summary>
public static class GeoCookies
{
    public const string Ciudad = "sk_ciudad";
    public const string Todas = "sk_ciudad_all";

    /// <summary>Departamento guardado en cookie (nunca coordenadas), ya normalizado.</summary>
    public static string? LeerCiudad(HttpRequest request)
    {
        var valor = request.Cookies[Ciudad];
        if (string.IsNullOrWhiteSpace(valor)) return null;

        try
        {
            valor = Uri.UnescapeDataString(valor);
        }
        catch (UriFormatException)
        {
            // Cookie manual o corrupta: se ignora.
        }

        return Catalogos.NormalizarDepartamento(valor);
    }

    public static bool EsTodas(HttpRequest request)
        => string.Equals(request.Cookies[Todas], "1", StringComparison.Ordinal);
}
