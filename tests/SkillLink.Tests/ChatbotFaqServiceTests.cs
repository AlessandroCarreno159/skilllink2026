using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using SkillLink_dotnet.Services.Chatbot;

namespace SkillLink.Tests;

public sealed class ChatbotFaqServiceTests
{
    [Fact]
    public void Buscar_EncuentraPostulacion()
    {
        var faq = CrearServicio();

        var resultados = faq.Buscar("¿Cómo postulo a una oferta?");

        Assert.Contains(resultados, entrada => entrada.Id == "postular");
    }

    [Fact]
    public void Buscar_EncuentraSoporte()
    {
        var faq = CrearServicio();

        var resultados = faq.Buscar("Necesito soporte porque no funciona");

        Assert.Contains(resultados, entrada => entrada.Id == "soporte");
    }

    private static ChatbotFaqService CrearServicio()
    {
        var environment = new TestWebHostEnvironment
        {
            ContentRootPath = EncontrarRaizProyecto(),
        };
        return new ChatbotFaqService(environment, NullLogger<ChatbotFaqService>.Instance);
    }

    private static string EncontrarRaizProyecto()
    {
        var directorio = new DirectoryInfo(AppContext.BaseDirectory);
        while (directorio is not null)
        {
            if (File.Exists(Path.Combine(directorio.FullName, "SkillLink-dotnet.csproj")))
                return directorio.FullName;
            directorio = directorio.Parent;
        }
        throw new InvalidOperationException("No se encontró la raíz del proyecto SkillLink.");
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "SkillLink.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
