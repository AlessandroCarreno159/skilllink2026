using System.Globalization;
using System.Text;
using SkillLink_dotnet.Models;

namespace SkillLink_dotnet.Services;

/// <summary>Etiquetas y catálogos del mockup (filtros, tarjetas, formularios).</summary>
public static class Catalogos
{
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

    public static string Precio(Publicacion p)
        => NormalizarPrecio(p.PrecioTexto);

    /// <summary>Normaliza a S/90 | S/90 - S/180 (coma decimal). Lo no reconocido se muestra tal cual.</summary>
    public static string NormalizarPrecio(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return "A convenir";
        var t = System.Text.RegularExpressions.Regex.Replace(v.Trim(), @"\s+", " ");
        t = System.Text.RegularExpressions.Regex.Replace(t, @"S/\s+", "S/");
        t = System.Text.RegularExpressions.Regex.Replace(t, @"\s*-\s*", " - ");
        var m = System.Text.RegularExpressions.Regex.Match(t, @"^(\d+(,\d+)?)\s*(?:-\s*S?/?\s*(\d+(,\d+)?))?$");
        if (!m.Success) return t;
        return string.IsNullOrEmpty(m.Groups[3].Value) ? $"S/{m.Groups[1].Value}" : $"S/{m.Groups[1].Value} - S/{m.Groups[3].Value}";
    }
}
