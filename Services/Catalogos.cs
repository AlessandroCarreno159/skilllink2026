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
