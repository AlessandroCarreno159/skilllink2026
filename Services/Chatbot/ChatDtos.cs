using System.Text.Json.Nodes;

namespace SkillLink_dotnet.Services.Chatbot;

public sealed record ChatUserContext(string UserId, string Role, string DisplayName);

public sealed record ChatMessageRequest(string? Message, string? ConversationId);

public sealed record ChatFeedbackRequest(string? ConversationId, string? MessageId, int Rating, string? Comment);

public sealed record ChatSessionMessage(string Role, string Text, DateTimeOffset At);

public sealed record ChatSession(string ConversationId, List<ChatSessionMessage> Messages);

public sealed record ChatResult(
    string ConversationId,
    string MessageId,
    string Reply,
    IReadOnlyList<JsonNode> Cards,
    IReadOnlyList<string> Suggestions);

public sealed record ChatTurn(string Role, string Text);

public sealed record ChatToolContext(string UserId, string Role, string ConversationId, string MessageId);

public sealed record ChatModelReply(string Reply, IReadOnlyList<JsonNode> Cards);

public sealed class ChatbotException(string message, int statusCode = 503, int retryAfterSeconds = 0) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public int RetryAfterSeconds { get; } = retryAfterSeconds;
}
