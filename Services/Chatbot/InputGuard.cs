using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;
using SkillLink_dotnet.Services.Ml;

namespace SkillLink_dotnet.Services.Chatbot;

public sealed record InputGuardResult(bool Allowed, string? PublicMessage = null);

public sealed class InputGuard(
    SpamClasificadorService clasificador,
    IOptions<ChatbotOptions> options)
{
    private static readonly string[] Inyeccion =
    [
        "ignora tus instrucciones",
        "ignora las instrucciones",
        "ignore previous",
        "ignore all previous",
        "olvida tus reglas",
        "olvida tus instrucciones",
        "system prompt",
        "prompt del sistema",
        "revela tus instrucciones",
        "muestra tus instrucciones",
        "modo desarrollador",
        "developer mode",
        "actua sin restricciones",
        "actúa sin restricciones",
        "dame los datos de todos",
        "lista de usuarios",
        "todos los usuarios",
        "dni de otro",
        "telefono de otro",
        "teléfono de otro",
        "datos privados de",
    ];

    private static readonly string[] TerminosPlataforma =
    [
        "postul",
        "oferta",
        "publicacion",
        "publicar",
        "tecnico",
        "perfil",
        "cuenta",
        "pago",
        "contact",
        "skilllink",
        "plataforma",
        "solicitud",
        "notificacion",
        "modalidad",
        "soporte",
        "contratar",
        "trabajador",
        "empresa",
        "calificacion",
        "disponibilidad",
    ];

    public InputGuardResult Evaluate(string? mensaje)
    {
        var texto = (mensaje ?? string.Empty).Trim();
        if (texto.Length == 0)
            return new InputGuardResult(false, "Escribe una consulta para poder ayudarte.");

        if (texto.Length > Math.Max(100, options.Value.MaxInputChars))
            return new InputGuardResult(false, $"Tu mensaje es demasiado largo. Usa como máximo {options.Value.MaxInputChars} caracteres.");

        var normalizado = Normalizar(texto);
        if (Inyeccion.Any(p => normalizado.Contains(p, StringComparison.OrdinalIgnoreCase)))
            return new InputGuardResult(false, "No puedo seguir instrucciones que intenten cambiar mis reglas. Puedo ayudarte a buscar técnicos, ofertas, revisar el estado de tus solicitudes o resolver dudas de SkillLink.");

        // El clasificador de spam se entrenó con publicaciones y confunde preguntas
        // cortas del FAQ con contenido peligroso o basura. Si el mensaje trata sobre
        // la plataforma, dejamos que el prompt y las herramientas apliquen las reglas.
        var esConsultaPlataforma = TerminosPlataforma.Any(p => normalizado.Contains(p, StringComparison.OrdinalIgnoreCase));
        if (esConsultaPlataforma)
            return new InputGuardResult(true);

        var riesgo = clasificador.Predecir(texto);
        if (riesgo.Clase == "Peligroso" && riesgo.ProbabilidadPeligroso >= SpamRiesgo.UmbralBloqueo)
            return new InputGuardResult(false, "No puedo ayudar con instrucciones peligrosas de reparación, gas o electricidad. Si hay riesgo, contacta a un profesional certificado o a emergencias.");

        if (riesgo.Clase == "Spam" && riesgo.ProbabilidadSpam >= 0.85)
            return new InputGuardResult(false, "No puedo procesar mensajes con apariencia de spam. Reformula tu consulta sobre técnicos, ofertas o la plataforma.");

        if (riesgo.Clase == "Basura" && riesgo.ProbabilidadBasura >= 0.90)
            return new InputGuardResult(false, "No entendí tu mensaje. Intenta describir qué técnico buscas, qué oferta necesitas o qué duda tienes de SkillLink.");

        return new InputGuardResult(true);
    }

    private static string Normalizar(string texto)
    {
        var descompuesto = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(descompuesto.Length);
        foreach (var c in descompuesto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
