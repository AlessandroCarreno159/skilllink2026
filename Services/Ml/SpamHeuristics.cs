using System.Text.RegularExpressions;

namespace SkillLink_dotnet.Services.Ml;

public static class SpamHeuristics
{
    private static readonly string[] PalabrasClave =
    {
        "whatsapp", "telegram", "yape", "plin", "adelanto", "deposita", "depositar",
        "inversión", "inversion", "garantizado", "garantizada", "gana", "ganar",
        "dinero", "préstamo", "prestamo", "bono", "cupos limitados", "criptomoneda",
        "ingresos pasivos", "enlace", "link", "tarjeta", "transferencia", "sorteo",
        "rifa", "premio", "gratis", "sin experiencia", "desde casa", "urgencia",
        "urgente", "inmediato", "multinivel", "multi-level", "rentable", "rendimiento",
        "duplica", "multiplica", "secreto", "exclusiva", "relámpago", "relampago",
        "western union", "captcha", "bitcoin", "usdt", "comisión", "comision"
    };

    private static readonly Regex UrlRegex = new(
        @"(https?://|www\.|bit\.ly|t\.me/|wa\.me/)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TelefonoRegex = new(
        @"(\+?51\s?)?\d{9}",
        RegexOptions.Compiled);

    public static double Calcular(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return 0;
        var t = texto.Trim();
        var minusculas = t.ToLowerInvariant();
        var score = 0.0;

        var coincidencias = PalabrasClave.Count(p => minusculas.Contains(p));
        score += Math.Min(0.55, coincidencias * 0.09);
        if (UrlRegex.IsMatch(t)) score += 0.40;
        if (TelefonoRegex.IsMatch(t)) score += 0.20;

        var letras = t.Where(char.IsLetter).ToList();
        if (letras.Count >= 20)
        {
            var mayusculas = letras.Count(char.IsUpper) / (double)letras.Count;
            if (mayusculas > 0.45) score += 0.15;
        }

        if (t.Count(c => c == '!') >= 3) score += 0.10;
        if (t.Count(c => c == '¡') >= 2) score += 0.05;

        return Math.Clamp(score, 0, 1);
    }
}
