using System.Globalization;
using System.Text;
using System.Text.Json;

namespace SkillLink_dotnet.Services.Chatbot;

public sealed record FaqEntry(string Id, string[] Preguntas, string Respuesta);

public sealed class ChatbotFaqService(
    IWebHostEnvironment environment,
    ILogger<ChatbotFaqService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly object _sync = new();
    private List<FaqEntry>? _entradas;

    public IReadOnlyList<FaqEntry> Buscar(string pregunta, int limite = 3)
    {
        var normalizada = Normalizar(pregunta);
        if (normalizada.Length == 0) return [];

        var entradas = Cargar();
        return entradas
            .Select(e => new { Entrada = e, Puntaje = Puntaje(normalizada, e) })
            .Where(x => x.Puntaje > 0)
            .OrderByDescending(x => x.Puntaje)
            .Take(Math.Clamp(limite, 1, 5))
            .Select(x => x.Entrada)
            .ToList();
    }

    public IReadOnlyList<FaqEntry> Todas() => Cargar();

    private List<FaqEntry> Cargar()
    {
        if (_entradas is not null) return _entradas;
        lock (_sync)
        {
            if (_entradas is not null) return _entradas;

            var ruta = Path.Combine(environment.ContentRootPath, "Data", "chatbot-faq.json");
            try
            {
                if (!File.Exists(ruta))
                {
                    logger.LogWarning("No se encontró {Ruta}; el FAQ del chatbot estará vacío.", ruta);
                    _entradas = [];
                    return _entradas;
                }

                var json = File.ReadAllText(ruta);
                _entradas = JsonSerializer.Deserialize<List<FaqEntry>>(json, JsonOptions) ?? [];
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "No se pudo cargar el FAQ del chatbot.");
                _entradas = [];
            }
            return _entradas;
        }
    }

    private static int Puntaje(string pregunta, FaqEntry entrada)
    {
        var puntaje = 0;
        foreach (var clave in entrada.Preguntas)
        {
            var normalizada = Normalizar(clave);
            if (normalizada.Length > 0 && pregunta.Contains(normalizada, StringComparison.OrdinalIgnoreCase))
                puntaje += normalizada.Length + 2;
        }

        foreach (var palabra in pregunta.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (palabra.Length < 4) continue;
            if (entrada.Preguntas.Any(p => Normalizar(p).Contains(palabra, StringComparison.OrdinalIgnoreCase)))
                puntaje += 1;
        }
        return puntaje;
    }

    private static string Normalizar(string texto)
    {
        var descompuesto = texto.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(descompuesto.Length);
        foreach (var c in descompuesto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
