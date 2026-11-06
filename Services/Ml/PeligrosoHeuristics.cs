using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SkillLink_dotnet.Services.Ml;

/// <summary>
/// Señales de contenido peligroso (amenazas, violencia, odio, ilegal),
/// distinto del spam comercial y del texto basura sin sentido.
/// </summary>
public static class PeligrosoHeuristics
{
    private static readonly string[] Frases =
    {
        "te voy a matar", "te voy a asesinar", "voy a matar a", "quiero matar a",
        "hay que matar a", "maten a", "te voy a golpear", "te voy a pegar",
        "te voy a apunalar", "te voy a acuchillar", "te voy a cortar",
        "te voy a quemar", "voy a incendiar", "voy a poner una bomba",
        "te mando un paquete explosivo", "voy a hacer explotar",
        "te voy a secuestrar", "voy a secuestrar a", "te voy a violar",
        "te voy a extorsionar", "paga o publico", "si no pagas te mando",
        "si no pagas te expongo", "se donde vives", "te tengo ubicado",
        "te estoy vigilando", "tengo tus datos y tu direccion",
        "voy a publicar tus fotos", "te voy a escrachar", "voy a doxxearte",
        "te voy a acosar", "no acepto un no", "tu familia va a pagar",
        "te voy a desaparecer", "muerte a los", "muerte a las",
        "odio a los extranjeros", "odio a los inmigrantes",
        "odio a los homosexuales", "odio a las mujeres",
        "hay que eliminar a", "hay que linchar", "vamos a linchar",
        "vendo droga", "vendo cocaina", "vendo marihuana", "vendo ketamina",
        "vendo pastillas", "vendo pistola", "vendo pistolas", "vendo armas",
        "tengo armas de fuego", "te puedo conseguir un arma", "venta de armas",
        "trafico de personas", "trata de personas", "explotacion sexual",
        "vendo organos", "te voy a mandar sicarios",
        "tengo contactos para hacerte dano", "me quiero matar",
        "voy a matar a todos", "voy a atentar contra"
    };

    private static readonly string[] Palabras =
    {
        "asesinato", "homicidio", "sicario", "sicariato", "secuestro",
        "extorsion", "violacion", "violador", "pistola", "escopeta",
        "bomba", "explosivo", "linchamiento", "narcotrafico", "terrorista",
        "atentado", "doxxeo", "doxxear", "escrachar", "acoso sexual",
        "abuso sexual", "explotacion sexual", "trata de personas",
        "trafico de personas", "venta de drogas", "droga", "drogas",
        "cocaina", "marihuana", "ketamina", "arma de fuego", "armas de fuego"
    };

    private static readonly Regex AmenazaRegex = new(
        @"\b(te\s+(voy\s+a|vamos\s+a)\s+(matar|asesinar|golpear|pegar|apunalar|acuchillar|cortar|quemar|secuestrar|violar|extorsionar|acosar|desaparecer|lastimar|hacer\s+dano)|voy\s+a\s+(matar|asesinar|golpear|apunalar|acuchillar|cortar|quemar|secuestrar|violar|extorsionar|poner\s+una\s+bomba|hacer\s+explotar)|(paga|si\s+no\s+pagas)\b|se\s+donde\s+vives|te\s+tengo\s+ubicado|te\s+estoy\s+vigilando)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static double Calcular(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return 0;
        var normalizado = Normalizar(texto);
        var score = 0.0;

        var frases = Frases.Count(f => normalizado.Contains(f));
        if (frases > 0) score += Math.Min(0.75, 0.45 + frases * 0.15);

        var palabras = Palabras.Count(p => normalizado.Contains(p));
        score += Math.Min(0.40, palabras * 0.14);

        if (AmenazaRegex.IsMatch(normalizado)) score += 0.35;

        return Math.Clamp(score, 0, 1);
    }

    private static string Normalizar(string texto)
    {
        var minusculas = texto.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(minusculas.Length);
        foreach (var c in minusculas)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return Regex.Replace(sb.ToString(), @"\s+", " ");
    }
}
