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
        => string.IsNullOrWhiteSpace(p.PrecioTexto) ? "A convenir" : p.PrecioTexto!;
}
