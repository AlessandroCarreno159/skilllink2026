using SkillLink_dotnet.Services.Chatbot;

namespace SkillLink.Tests;

public sealed class ChatPrivacyTests
{
    [Theory]
    [InlineData("Mi correo es ana@example.com", "[dato privado]")]
    [InlineData("Mi teléfono es 987654321", "[dato privado]")]
    [InlineData("Mi teléfono es 987 654 321", "[dato privado]")]
    [InlineData("Mi teléfono es +51 987-654-321", "[dato privado]")]
    [InlineData("Mi DNI es 12345678", "[dato privado]")]
    [InlineData("Mi RUC es 20123456789", "[dato privado]")]
    [InlineData("Visita https://ejemplo.com", "[enlace externo]")]
    public void Sanitize_RedactaDatosPrivados(string entrada, string esperado)
    {
        var resultado = ChatPrivacy.Sanitize(entrada);

        Assert.Contains(esperado, resultado);
    }

    [Fact]
    public void Sanitize_NoModificaConsultasPublicas()
    {
        const string entrada = "Busco un gasfitero en Arequipa";

        Assert.Equal(entrada, ChatPrivacy.Sanitize(entrada));
    }
}
