using SkillLink_dotnet.Models;
using SkillLink_dotnet.Services.Chatbot;

namespace SkillLink.Tests;

public sealed class ChatbotPromptsTests
{
    [Fact]
    public void System_IndicaBotonDeSoporteSinEnlaces()
    {
        var prompt = ChatbotPrompts.System(Roles.Trabajador, "Ana");

        Assert.Contains("Soporte por WhatsApp", prompt);
        Assert.Contains("No escribas números de teléfono, URLs ni enlaces Markdown", prompt);
        Assert.DoesNotContain("http", prompt.ToLowerInvariant());
        Assert.DoesNotContain("wa.me", prompt.ToLowerInvariant());
    }
}
