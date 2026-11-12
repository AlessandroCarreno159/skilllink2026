using System.Globalization;
using System.Text;
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

    private static readonly string[] PrefijosGeo =
    {
        "region de", "region", "departamento de", "departamento",
        "provincia constitucional del", "provincia constitucional de",
        "provincia de", "provincia", "province of", "province",
        "department of", "department", "state of", "state",
        "estado de", "estado", "distrito de", "distrito",
        "municipalidad de", "municipalidad",
    };

    private static readonly string[] SufijosGeo =
    {
        "region", "province", "department", "state", "departamento", "provincia", "distrito",
    };

    private static readonly Dictionary<string, string> DepartamentosNormalizados =
        Departamentos.ToDictionary(NormalizarTexto, d => d, StringComparer.Ordinal);

    /// <summary>
    /// Convierte un nombre geográfico de Nominatim (por ejemplo "Province of Lima"
    /// o "Región de Áncash") al departamento canónico de <see cref="Departamentos"/>.
    /// Devuelve null cuando no corresponde a un departamento peruano conocido.
    /// </summary>
    public static string? NormalizarDepartamento(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;

        var texto = QuitarSufijos(QuitarPrefijos(NormalizarTexto(valor)));
        if (DepartamentosNormalizados.TryGetValue(texto, out var departamento)) return departamento;

        // Nominatim puede enviar "Lima, Departamento de Lima, Perú" en display_name.
        foreach (var parte in valor.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidato = QuitarSufijos(QuitarPrefijos(NormalizarTexto(parte)));
            if (DepartamentosNormalizados.TryGetValue(candidato, out departamento)) return departamento;
        }

        return null;
    }

    private static string NormalizarTexto(string valor)
    {
        var descompuesto = valor.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(descompuesto.Length);
        var separadorPendiente = false;

        foreach (var c in descompuesto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;

            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
                separadorPendiente = false;
            }
            else if (!separadorPendiente && sb.Length > 0)
            {
                sb.Append(' ');
                separadorPendiente = true;
            }
        }

        return sb.ToString().Trim();
    }

    private static string QuitarPrefijos(string texto)
    {
        var cambiado = true;
        while (cambiado && texto.Length > 0)
        {
            cambiado = false;
            foreach (var prefijo in PrefijosGeo)
            {
                if (texto == prefijo)
                {
                    texto = string.Empty;
                    cambiado = true;
                    break;
                }
                if (texto.StartsWith(prefijo + " ", StringComparison.Ordinal))
                {
                    texto = texto[(prefijo.Length + 1)..].Trim();
                    cambiado = true;
                    break;
                }
            }
        }
        return texto;
    }

    private static string QuitarSufijos(string texto)
    {
        var cambiado = true;
        while (cambiado && texto.Length > 0)
        {
            cambiado = false;
            foreach (var sufijo in SufijosGeo)
            {
                if (texto.Length <= sufijo.Length + 1) continue;
                if (!texto.EndsWith(" " + sufijo, StringComparison.Ordinal)) continue;
                texto = texto[..^(sufijo.Length + 1)].Trim();
                cambiado = true;
                break;
            }
        }
        return texto;
    }

    public static readonly Dictionary<string, string> Tipos = new()
    {
        [TiposPublicacion.Oferta] = "Oferta",
        [TiposPublicacion.Convocatoria] = "Convocatoria",
        [TiposPublicacion.Experiencia] = "Experiencia",
        [TiposPublicacion.Busqueda] = "Búsqueda",
        [TiposPublicacion.Proyecto] = "Proyecto",
        [TiposPublicacion.Anuncio] = "Anuncio",
    };

    public static string Categoria(string? v)
        => !string.IsNullOrWhiteSpace(v) && Categorias.TryGetValue(v, out var l) ? l : "General";

    public static string Modalidad(string? v)
        => !string.IsNullOrWhiteSpace(v) && Modalidades.TryGetValue(v, out var l) ? l : "—";

    public static string Tipo(string? v)
        => !string.IsNullOrWhiteSpace(v) && Tipos.TryGetValue(v, out var l) ? l : (v ?? "Publicación");

    public static bool PermitePrecio(string? tipo)
        => !EsTipo(tipo, TiposPublicacion.Experiencia)
        && !EsTipo(tipo, TiposPublicacion.Proyecto)
        && !EsTipo(tipo, TiposPublicacion.Anuncio);

    public static bool PermiteVacantes(string? tipo)
        => !EsTipo(tipo, TiposPublicacion.Experiencia)
        && !EsTipo(tipo, TiposPublicacion.Anuncio);

    public static bool PermiteHabilidades(string? tipo)
        => !EsTipo(tipo, TiposPublicacion.Anuncio);

    public static bool PrecioEsOpcional(string? tipo)
        => EsTipo(tipo, TiposPublicacion.Busqueda);

    public static string EtiquetaHabilidades(string? tipo)
        => EsTipo(tipo, TiposPublicacion.Experiencia) || EsTipo(tipo, TiposPublicacion.Busqueda)
            ? "Habilidades ofrecidas (separadas por comas)"
            : "Habilidades requeridas (separadas por comas)";

    public static string DescripcionTipo(string? tipo)
        => tipo switch
        {
            TiposPublicacion.Oferta => "Ofrece tus servicios o tu disponibilidad para trabajar. Publica tu perfil y deja que empresas o clientes te contacten.",
            TiposPublicacion.Convocatoria => "Convoca a personas o personal para cubrir una necesidad. Ideal para empresas o equipos que buscan contratar.",
            TiposPublicacion.Experiencia => "Comparte trabajos o proyectos que ya realizaste. Adjunta evidencias de tu experiencia para mostrar tu trayectoria.",
            TiposPublicacion.Busqueda => "Publica que buscas un trabajo en específico, o que buscas a una persona para un puesto o servicio determinado.",
            TiposPublicacion.Proyecto => "Anuncia tu propio proyecto o iniciativa para convocar a personas que quieran sumarse y colaborar.",
            TiposPublicacion.Anuncio => "Informa noticias, novedades o comunicados relacionados con tu empresa, comunidad o actividad.",
            _ => "Selecciona el tipo de publicación que mejor describa lo que quieres compartir.",
        };

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
        if (!PermitePrecio(p.Tipo)) return "—";
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
        if (!PermitePrecio(p.Tipo))
            return new PrecioVisible("—", null, etiqueta);
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

    private static bool EsTipo(string? tipo, string esperado)
        => string.Equals(tipo, esperado, StringComparison.OrdinalIgnoreCase);

    private static bool TryParseNumero(string s, out decimal value)
        => decimal.TryParse(
            s.Replace(',', '.'),
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out value);

    private static string Numero(decimal value)
        => value.ToString("0.##", CultureInfo.InvariantCulture);
}
