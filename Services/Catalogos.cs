using System.Globalization;
using SkillLink_dotnet.Models;

namespace SkillLink_dotnet.Services;

/// <summary>Etiquetas y catálogos del mockup (filtros, tarjetas, formularios).</summary>
public static class Catalogos
{
    public readonly record struct PrecioVisible(string Principal, string? Secundario, string EtiquetaMoneda);

    public static readonly Dictionary<string, string> Categorias = new()
    {
        ["construccion"] = "Construcción",
        ["tecnologia"] = "Tecnología",
        ["diseno"] = "Diseño",
        ["hogar"] = "Hogar",
        ["otros"] = "Otros",
    };

    public static readonly Dictionary<string, string> Modalidades = new()
    {
        ["tiempo_completo"] = "Tiempo completo",
        ["medio_tiempo"] = "Medio tiempo",
        ["por_proyecto"] = "Por proyecto",
    };

    /// <summary>Departamentos del Perú para el filtro de ubicación.</summary>
    public static readonly string[] Departamentos = new[]
    {
        "Amazonas", "Áncash", "Apurímac", "Arequipa", "Ayacucho", "Cajamarca",
        "Callao", "Cusco", "Huancavelica", "Huánuco", "Ica", "Junín",
        "La Libertad", "Lambayeque", "Lima", "Loreto", "Madre de Dios",
        "Moquegua", "Pasco", "Piura", "Puno", "San Martín", "Tacna",
        "Tumbes", "Ucayali",
    };

    public static readonly Dictionary<string, string> Tipos = new()
    {
        [TiposPublicacion.Oferta] = "Oferta",
        ["convocatoria"] = "Convocatoria",
        ["experiencia"] = "Experiencia",
        ["busqueda"] = "Búsqueda",
        ["proyecto"] = "Proyecto",
        ["empresa_info"] = "Anuncio",
    };

    public static string Categoria(string? v)
        => !string.IsNullOrWhiteSpace(v) && Categorias.TryGetValue(v, out var l) ? l : "General";

    public static string Modalidad(string? v)
        => !string.IsNullOrWhiteSpace(v) && Modalidades.TryGetValue(v, out var l) ? l : "—";

    public static string Tipo(string? v)
        => !string.IsNullOrWhiteSpace(v) && Tipos.TryGetValue(v, out var l) ? l : (v ?? "Publicación");

    public static string Ciudad(Publicacion p)
        => p.Autor?.Perfil?.Ciudad ?? p.Autor?.Empresa?.Ciudad ?? "—";

    public static bool EsUsd(string? moneda)
        => string.Equals(moneda, Monedas.Usd, StringComparison.OrdinalIgnoreCase);

    public static string MonedaBase(Publicacion p)
        => EsUsd(p.Moneda) ? Monedas.Usd : Monedas.Pen;

    public static string SimboloMoneda(string? moneda)
        => EsUsd(moneda) ? "US$" : "S/";

    public static string EtiquetaMoneda(Publicacion p)
        => p.PrecioDual
            ? "Acepta soles y dólares"
            : (EsUsd(p.Moneda) ? "Solo dólares" : "Solo soles");

    public static string Precio(Publicacion p)
    {
        var moneda = MonedaBase(p);
        return TryParsePrecio(p.PrecioTexto, out var min, out var max)
            ? FormatearPrecio(min, max, moneda)
            : "A convenir";
    }

    /// <summary>
    /// Precio principal en la moneda base, precio secundario aproximado (si
    /// PrecioDual está activo y hay tasa) y la etiqueta de monedas aceptadas.
    /// </summary>
    public static PrecioVisible PrecioMostrado(Publicacion p, decimal? penToUsd)
    {
        var moneda = MonedaBase(p);
        var etiqueta = EtiquetaMoneda(p);
        if (!TryParsePrecio(p.PrecioTexto, out var min, out var max))
            return new PrecioVisible("A convenir", null, etiqueta);

        var principal = FormatearPrecio(min, max, moneda);
        string? secundario = null;
        if (p.PrecioDual && penToUsd is > 0)
        {
            var target = EsUsd(moneda) ? Monedas.Pen : Monedas.Usd;
            var factor = EsUsd(moneda) ? 1m / penToUsd.Value : penToUsd.Value;
            secundario = "≈ " + FormatearPrecio(min * factor, max * factor, target);
        }
        return new PrecioVisible(principal, secundario, etiqueta);
    }

    /// <summary>Normaliza el texto del precio a la moneda indicada para guardarlo en BD.</summary>
    public static string? PrecioParaGuardar(string? v, string? moneda)
        => TryParsePrecio(v, out var min, out var max)
            ? FormatearPrecio(min, max, moneda)
            : null;

    public static bool TryParsePrecio(string? v, out decimal min, out decimal? max)
    {
        min = 0m;
        max = null;
        if (string.IsNullOrWhiteSpace(v)) return false;
        if (string.Equals(v.Trim(), "A convenir", StringComparison.OrdinalIgnoreCase)) return false;

        var matches = System.Text.RegularExpressions.Regex.Matches(v, @"\d+(?:[.,]\d+)?");
        if (matches.Count == 0) return false;
        if (!TryParseNumero(matches[0].Value, out min)) return false;
        if (matches.Count > 1 && TryParseNumero(matches[1].Value, out var m)) max = m;
        return true;
    }

    public static string FormatearPrecio(decimal min, decimal? max, string? moneda)
    {
        var simbolo = SimboloMoneda(moneda);
        var textoMin = Numero(min);
        if (max is not > 0 || max.Value == min)
            return $"{simbolo}{textoMin}";
        return $"{simbolo}{textoMin} - {simbolo}{Numero(max.Value)}";
    }

    /// <summary>Normaliza a S/90 | S/90 - S/180. Lo no reconocido se muestra como "A convenir".</summary>
    public static string NormalizarPrecio(string? v)
    {
        if (!TryParsePrecio(v, out var min, out var max)) return "A convenir";
        var moneda = v is not null && v.Contains("US$", StringComparison.OrdinalIgnoreCase)
            ? Monedas.Usd
            : Monedas.Pen;
        return FormatearPrecio(min, max, moneda);
    }

    private static bool TryParseNumero(string s, out decimal value)
        => decimal.TryParse(
            s.Replace(',', '.'),
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out value);

    private static string Numero(decimal value)
        => value.ToString("0.##", CultureInfo.InvariantCulture);
}
