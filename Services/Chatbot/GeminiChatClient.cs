using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using SkillLink_dotnet.Services;

namespace SkillLink_dotnet.Services.Chatbot;

public interface IGeminiChatClient
{
    Task<ChatModelReply> ChatAsync(
        ChatUserContext user,
        IReadOnlyList<ChatTurn> history,
        string message,
        ChatToolContext toolContext,
        Func<string, Task> onToken,
        CancellationToken ct);
}

public sealed class GeminiChatClient(
    HttpClient http,
    IOptions<ChatbotOptions> options,
    ChatToolRegistry tools,
    RealtimePublisher realtime,
    ILogger<GeminiChatClient> logger) : IGeminiChatClient
{
    private const string FallbackReply =
        "No pude generar una respuesta en este momento. Intenta reformular tu consulta o vuelve a probar en unos segundos.";

    private bool _usarFallback;

    public async Task<ChatModelReply> ChatAsync(
        ChatUserContext user,
        IReadOnlyList<ChatTurn> history,
        string message,
        ChatToolContext toolContext,
        Func<string, Task> onToken,
        CancellationToken ct)
    {
        var cfg = options.Value;
        if (!cfg.IsConfigured)
            throw new ChatbotException("El asistente no está configurado. Contacta al administrador.", 503);

        _usarFallback = false;
        var contents = BuildContents(cfg, history, message);
        var declarations = BuildTools(user.Role);
        var cards = new List<JsonNode>();
        var maxTools = Math.Clamp(cfg.MaxToolCalls, 0, 8);
        var toolCalls = 0;

        for (var intento = 0; intento <= maxTools + 1; intento++)
        {
            var incluirHerramientas = declarations.Count > 0 && toolCalls < maxTools;
            var respuesta = await GenerateAsync(cfg, user, contents, incluirHerramientas ? declarations : null, ct);
            var partes = respuesta?["candidates"]?[0]?["content"]?["parts"]?.AsArray();

            if (partes is null || partes.Count == 0)
            {
                logger.LogWarning("Gemini no devolvió partes. Respuesta: {Respuesta}", respuesta?.ToJsonString());
                return new ChatModelReply(FallbackReply, cards);
            }

            var llamadas = partes
                .Select(p => p?["functionCall"])
                .Where(fc => fc is not null)
                .ToList();

            if (llamadas.Count == 0)
            {
                var texto = ChatPrivacy.Sanitize(ExtraerTexto(partes));
                if (string.IsNullOrWhiteSpace(texto)) texto = FallbackReply;
                await EmitirTokensAsync(texto, onToken, ct);
                return new ChatModelReply(texto.Trim(), cards);
            }

            var partesModelo = new JsonArray();
            foreach (var parte in partes)
            {
                var copia = parte?.DeepClone();
                if (copia is not null) partesModelo.Add(copia);
            }
            contents.Add(new JsonObject { ["role"] = "model", ["parts"] = partesModelo });

            var respuestas = new JsonArray();
            foreach (var llamada in llamadas)
            {
                var nombre = llamada?["name"]?.GetValue<string>() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(nombre)) continue;

                if (toolCalls >= maxTools)
                {
                    respuestas.Add(RespuestaFuncion(nombre, new JsonObject
                    {
                        ["ok"] = false,
                        ["error"] = "Máximo de consultas internas alcanzado.",
                    }));
                    continue;
                }

                toolCalls++;
                await PublicarToolAsync(toolContext.UserId, nombre, "running");
                var resultado = await tools.ExecuteAsync(toolContext, nombre, llamada?["args"], ct);
                await PublicarToolAsync(toolContext.UserId, nombre, "done");
                RecolectarTarjetas(resultado, cards);
                respuestas.Add(RespuestaFuncion(nombre, resultado));
            }

            contents.Add(new JsonObject { ["role"] = "user", ["parts"] = respuestas });

            if (toolCalls >= maxTools)
            {
                var final = await GenerateAsync(cfg, user, contents, null, ct);
                var partesFinales = final?["candidates"]?[0]?["content"]?["parts"]?.AsArray();
                var textoFinal = partesFinales is null ? string.Empty : ChatPrivacy.Sanitize(ExtraerTexto(partesFinales));
                if (string.IsNullOrWhiteSpace(textoFinal)) textoFinal = FallbackReply;
                await EmitirTokensAsync(textoFinal, onToken, ct);
                return new ChatModelReply(textoFinal.Trim(), cards);
            }
        }

        return new ChatModelReply(FallbackReply, cards);
    }

    private static JsonArray BuildContents(ChatbotOptions cfg, IReadOnlyList<ChatTurn> history, string message)
    {
        var contents = new JsonArray();
        var limite = Math.Max(0, cfg.MaxHistoryMessages);
        var recorte = history.Count > limite ? history.Skip(history.Count - limite) : history;
        foreach (var turno in recorte)
        {
            if (string.IsNullOrWhiteSpace(turno.Text)) continue;
            contents.Add(ContenidoTexto(turno.Role is "model" or "assistant" ? "model" : "user", turno.Text));
        }
        contents.Add(ContenidoTexto("user", message));
        return contents;
    }

    private JsonArray BuildTools(string role)
    {
        var declarations = new JsonArray();
        foreach (var tool in tools.ForRole(role))
        {
            var parametros = tool.Parameters.DeepClone();
            declarations.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["parameters"] = parametros ?? new JsonObject { ["type"] = "OBJECT", ["properties"] = new JsonObject() },
            });
        }
        return declarations;
    }

    private async Task<JsonNode?> GenerateAsync(
        ChatbotOptions cfg,
        ChatUserContext user,
        JsonArray contents,
        JsonArray? declarations,
        CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["text"] = ChatbotPrompts.System(user.Role, user.DisplayName, cfg.SupportUrl)
                    }
                }
            },
            ["contents"] = contents.DeepClone(),
            ["generationConfig"] = new JsonObject
            {
                ["temperature"] = 0.3,
                ["topP"] = 0.9,
                ["maxOutputTokens"] = Math.Clamp(cfg.MaxOutputTokens, 128, 2048),
                ["candidateCount"] = 1,
            },
            ["safetySettings"] = new JsonArray
            {
                new JsonObject { ["category"] = "HARM_CATEGORY_HARASSMENT", ["threshold"] = "BLOCK_MEDIUM_AND_ABOVE" },
                new JsonObject { ["category"] = "HARM_CATEGORY_HATE_SPEECH", ["threshold"] = "BLOCK_MEDIUM_AND_ABOVE" },
                new JsonObject { ["category"] = "HARM_CATEGORY_SEXUALLY_EXPLICIT", ["threshold"] = "BLOCK_MEDIUM_AND_ABOVE" },
                new JsonObject { ["category"] = "HARM_CATEGORY_DANGEROUS_CONTENT", ["threshold"] = "BLOCK_MEDIUM_AND_ABOVE" },
            },
        };

        if (declarations is { Count: > 0 })
            body["tools"] = new JsonArray { new JsonObject { ["functionDeclarations"] = declarations.DeepClone() } };

        var modelo = _usarFallback && !string.IsNullOrWhiteSpace(cfg.FallbackModel)
            ? cfg.FallbackModel
            : cfg.Model;
        var url = $"{cfg.BaseUrl.TrimEnd('/')}/models/{Uri.EscapeDataString(modelo)}:generateContent";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.TryAddWithoutValidation("x-goog-api-key", cfg.ApiKey);
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(cfg.TimeoutSeconds, 5, 120)));

        HttpResponseMessage response;
        string raw;
        try
        {
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            raw = await response.Content.ReadAsStringAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ChatbotException("El asistente tardó demasiado en responder. Intenta nuevamente.", 504);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "No se pudo conectar con Gemini.");
            throw new ChatbotException("No pude contactar al asistente en este momento. Intenta nuevamente.", 503);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Gemini respondió {Status}: {Body}", (int)response.StatusCode, Truncar(raw, 500));

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    if (!_usarFallback
                        && !string.IsNullOrWhiteSpace(cfg.FallbackModel)
                        && !string.Equals(cfg.FallbackModel, cfg.Model, StringComparison.OrdinalIgnoreCase))
                    {
                        _usarFallback = true;
                        logger.LogWarning("Gemini sin cuota para {Model}; se usará {Fallback}.",
                            cfg.Model, cfg.FallbackModel);
                        return await GenerateAsync(cfg, user, contents, declarations, ct);
                    }

                    var retry = 60;
                    if (response.Headers.RetryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
                        retry = Math.Max(1, (int)Math.Ceiling(delta.TotalSeconds));
                    throw new ChatbotException(
                        "El asistente recibió demasiadas solicitudes. Espera unos segundos y vuelve a intentar.",
                        429, retry);
                }

                throw new ChatbotException("El asistente no está disponible en este momento. Intenta nuevamente.", 503);
            }
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(raw);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Gemini devolvió JSON inválido.");
            throw new ChatbotException("El asistente devolvió una respuesta inválida. Intenta nuevamente.", 502);
        }

        if (root?["error"] is not null)
        {
            logger.LogWarning("Gemini devolvió error: {Error}", root["error"]?.ToJsonString());
            throw new ChatbotException("El asistente no está disponible en este momento. Intenta nuevamente.", 503);
        }

        return root;
    }

    private async Task PublicarToolAsync(string userId, string nombre, string estado)
    {
        await realtime.PublishAsync("chatbot:tool", userId, new { name = nombre, status = estado });
    }

    private static void RecolectarTarjetas(JsonNode resultado, List<JsonNode> cards)
    {
        var array = resultado?["cards"]?.AsArray();
        if (array is null) return;

        foreach (var card in array)
        {
            if (card is null) continue;
            var tipo = card["type"]?.ToString() ?? string.Empty;
            var id = card["id"]?.ToString() ?? string.Empty;
            if (cards.Any(c => (c["type"]?.ToString() ?? string.Empty) == tipo
                               && (c["id"]?.ToString() ?? string.Empty) == id))
                continue;

            var copia = card.DeepClone();
            if (copia is not null) cards.Add(copia);
            if (cards.Count >= 6) return;
        }
    }

    private static async Task EmitirTokensAsync(string texto, Func<string, Task> onToken, CancellationToken ct)
    {
        const int tamano = 24;
        for (var i = 0; i < texto.Length; i += tamano)
        {
            ct.ThrowIfCancellationRequested();
            var trozo = texto.Substring(i, Math.Min(tamano, texto.Length - i));
            await onToken(trozo);
            await Task.Delay(10, ct);
        }
    }

    private static string ExtraerTexto(JsonArray partes)
    {
        var sb = new StringBuilder();
        foreach (var parte in partes)
        {
            if (parte?["thought"]?.GetValue<bool>() == true) continue;
            var texto = parte?["text"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(texto)) sb.Append(texto);
        }
        return sb.ToString();
    }

    private static JsonObject ContenidoTexto(string rol, string texto) => new()
    {
        ["role"] = rol,
        ["parts"] = new JsonArray { new JsonObject { ["text"] = texto } },
    };

    private static JsonObject RespuestaFuncion(string nombre, JsonNode respuesta) => new()
    {
        ["functionResponse"] = new JsonObject
        {
            ["name"] = nombre,
            ["response"] = respuesta.DeepClone(),
        },
    };

    private static string Truncar(string texto, int max)
        => texto.Length <= max ? texto : texto[..max];
}
