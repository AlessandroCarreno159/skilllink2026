namespace SkillLink_dotnet.Services.Chatbot;

public sealed class ChatbotOptions
{
    public const string SectionName = "Chatbot";

    public bool Enabled { get; set; } = true;
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gemini-2.5-flash";
    public string FallbackModel { get; set; } = "gemini-flash-lite-latest";
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";
    public int MaxInputChars { get; set; } = 1000;
    public int MaxHistoryMessages { get; set; } = 20;
    public int SessionTtlMinutes { get; set; } = 120;
    public int DailyMessages { get; set; } = 30;
    public int BurstPerMinute { get; set; } = 5;
    public int MaxToolCalls { get; set; } = 4;
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxOutputTokens { get; set; } = 800;
    public string SupportUrl { get; set; } = string.Empty;

    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(ApiKey);
}
