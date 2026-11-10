using System.Text.RegularExpressions;

namespace SkillLink_dotnet.Services.Chatbot;

/// <summary>Redacta datos privados antes de enviarlos al modelo o al cliente.</summary>
public static class ChatPrivacy
{
    private static readonly Regex Correo = new(
        @"\b[\w.+-]+@[\w-]+\.[\w.-]+\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Enlace = new(
        @"\b(?:https?://|www\.)\S+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Telefono = new(
        @"(?<!\d)(?:\+?51[\s.-]?)?(?:9(?:[\s.-]?\d){8}|[67](?:[\s.-]?\d){8})(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Ruc = new(
        @"(?<!\d)\d{11}(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Dni = new(
        @"(?<!\d)\d{8}(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Sanitize(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return string.Empty;

        var valor = Correo.Replace(texto, "[dato privado]");
        valor = Enlace.Replace(valor, "[enlace externo]");
        valor = Telefono.Replace(valor, "[dato privado]");
        valor = Ruc.Replace(valor, "[dato privado]");
        valor = Dni.Replace(valor, "[dato privado]");
        return valor.Trim();
    }
}
