using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using SkillLink_dotnet.Models;
using SkillLink_dotnet.Services;

namespace SkillLink_dotnet.Services.Chatbot;

public sealed class ChatbotOrchestrator(
    IOptions<ChatbotOptions> options,
    ChatSessionStore store,
    ChatRateLimiter rateLimiter,
    InputGuard guard,
    IGeminiChatClient gemini,
    RealtimePublisher realtime,
    ILogger<ChatbotOrchestrator> logger)
{
    public async Task<ChatResult> SendAsync(
        ChatUserContext user,
        string? mensaje,
        string? conversationId,
        CancellationToken ct)
    {
        var cfg = options.Value;
        if (!cfg.IsConfigured)
            throw new ChatbotException("El asistente no está disponible en este momento.", 503);

        var rate = await rateLimiter.CheckAsync(user.UserId, ct);
        if (!rate.Allowed)
            throw new ChatbotException(rate.Reason ?? "Demasiados mensajes. Espera un momento.",
                429, rate.RetryAfterSeconds);

        var entrada = guard.Evaluate(mensaje);
        if (!entrada.Allowed)
        {
            var sesionBloqueada = await store.GetAsync(user.UserId, ct);
            var idBloqueado = Guid.NewGuid().ToString("N");
            var textoBloqueado = entrada.PublicMessage ?? "No puedo procesar ese mensaje.";
            var sinTarjetas = Array.Empty<JsonNode>();
            var sinSugerencias = BuildSuggestions(user.Role, sinTarjetas);
            await realtime.PublishAsync("chatbot:done", user.UserId, new
            {
                conversationId = sesionBloqueada.ConversationId,
                messageId = idBloqueado,
                reply = textoBloqueado,
                cards = sinTarjetas,
                suggestions = sinSugerencias,
            });
            return new ChatResult(sesionBloqueada.ConversationId, idBloqueado, textoBloqueado,
                sinTarjetas, sinSugerencias);
        }

        var texto = (mensaje ?? string.Empty).Trim();
        var session = await store.GetAsync(user.UserId, ct);
        if (!string.IsNullOrWhiteSpace(conversationId)
            && !string.Equals(conversationId, session.ConversationId, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("El cliente envió una conversación distinta ({Cliente}); se continúa con {Servidor}.",
                conversationId, session.ConversationId);
        }

        var messageId = Guid.NewGuid().ToString("N");
        var toolContext = new ChatToolContext(user.UserId, user.Role, session.ConversationId, messageId);
        var historial = session.Messages
            .Select(m => new ChatTurn(m.Role, m.Text))
            .ToList();

        async Task OnToken(string token)
        {
            await realtime.PublishAsync("chatbot:token", user.UserId, new
            {
                conversationId = session.ConversationId,
                messageId,
                text = token,
            });
        }

        ChatModelReply reply;
        try
        {
            reply = await gemini.ChatAsync(user, historial, texto, toolContext, OnToken, ct);
        }
        catch (ChatbotException ex)
        {
            await realtime.PublishAsync("chatbot:error", user.UserId, new
            {
                conversationId = session.ConversationId,
                messageId,
                message = ex.Message,
            });
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error inesperado del chatbot para {UserId}.", user.UserId);
            await realtime.PublishAsync("chatbot:error", user.UserId, new
            {
                conversationId = session.ConversationId,
                messageId,
                message = "Ocurrió un error inesperado. Intenta nuevamente.",
            });
            throw new ChatbotException("Ocurrió un error inesperado. Intenta nuevamente.", 500);
        }

        var respuestaSegura = ChatPrivacy.Sanitize(reply.Reply);
        if (string.IsNullOrWhiteSpace(respuestaSegura))
            respuestaSegura = "No pude generar una respuesta en este momento. Intenta reformular tu consulta.";
        reply = reply with { Reply = respuestaSegura };

        var sugerencias = BuildSuggestions(user.Role, reply.Cards);
        session.Messages.Add(new ChatSessionMessage("user", texto, DateTimeOffset.UtcNow));
        session.Messages.Add(new ChatSessionMessage("model", reply.Reply, DateTimeOffset.UtcNow));
        await store.SaveAsync(user.UserId, session, ct);

        await realtime.PublishAsync("chatbot:done", user.UserId, new
        {
            conversationId = session.ConversationId,
            messageId,
            reply = reply.Reply,
            cards = reply.Cards,
            suggestions = sugerencias,
        });

        return new ChatResult(session.ConversationId, messageId, reply.Reply, reply.Cards, sugerencias);
    }

    private static IReadOnlyList<string> BuildSuggestions(string role, IReadOnlyList<JsonNode> cards)
    {
        var tipos = cards
            .Select(c =>
            {
                try { return c["type"]?.ToString(); }
                catch { return null; }
            })
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (tipos.Contains("tecnico"))
            return ["Ver perfiles sugeridos", "Buscar en otra ciudad", "¿Cómo contacto a un técnico?"];
        if (tipos.Contains("publicacion"))
            return ["Ver las publicaciones", "¿Cómo postulo?", "Buscar otra oferta"];

        return role switch
        {
            Roles.Trabajador =>
            [
                "Buscar trabajo",
                "Estado de mis solicitudes",
                "¿Cómo mejoro mi perfil?",
            ],
            Roles.Empresa =>
            [
                "Buscar técnicos",
                "Postulaciones recibidas",
                "¿Cómo publico una oferta?",
            ],
            Roles.Admin =>
            [
                "Buscar publicaciones",
                "Preguntas frecuentes",
                "¿Cómo funciona el asistente?",
            ],
            _ =>
            [
                "Buscar técnicos",
                "Ver ofertas",
                "¿Qué es SkillLink?",
            ],
        };
    }
}
