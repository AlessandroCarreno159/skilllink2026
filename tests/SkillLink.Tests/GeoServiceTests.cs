using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SkillLink_dotnet.Services;
using SkillLink_dotnet.Services.Geo;

namespace SkillLink.Tests;

public sealed class GeoServiceTests
{
    [Theory]
    [InlineData("Lima", "Lima")]
    [InlineData("LIMA", "Lima")]
    [InlineData("Provincia de Lima", "Lima")]
    [InlineData("Province of Lima", "Lima")]
    [InlineData("Lima Province", "Lima")]
    [InlineData("Región de Áncash", "Áncash")]
    [InlineData("REGION DE ANCASH", "Áncash")]
    [InlineData("Departamento de Huánuco", "Huánuco")]
    [InlineData("Province of San Martin", "San Martín")]
    [InlineData("Provincia Constitucional del Callao", "Callao")]
    [InlineData("  región   de   cusco  ", "Cusco")]
    [InlineData("Departamento de Madre de Dios", "Madre de Dios")]
    public void NormalizarDepartamento_MapeaVariantesANombreCanonico(string entrada, string esperado)
    {
        Assert.Equal(esperado, Catalogos.NormalizarDepartamento(entrada));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Province of Madrid")]
    [InlineData("Ciudad inexistente")]
    public void NormalizarDepartamento_DevuelveNullSiNoEsDepartamentoPeruano(string entrada)
    {
        Assert.Null(Catalogos.NormalizarDepartamento(entrada));
    }

    [Theory]
    [InlineData(91, -77)]
    [InlineData(-91, -77)]
    [InlineData(-12, 181)]
    [InlineData(-12, -181)]
    public async Task ReverseCity_CoordenadasInvalidas_NoLlamaANominatim(double lat, double lon)
    {
        var handler = new StubHandler(_ => RespuestaJson("{}"));
        var servicio = CrearServicio(handler);

        var resultado = await servicio.ReverseCityAsync(lat, lon);

        Assert.Null(resultado);
        Assert.Equal(0, handler.Llamadas);
    }

    [Fact]
    public async Task ReverseCity_MapeaEstadoYEnviaUserAgent()
    {
        const string json = """
            {
              "display_name": "Arequipa, Perú",
              "address": { "state": "Región de Arequipa", "country": "Perú" }
            }
            """;
        var handler = new StubHandler(_ => RespuestaJson(json));
        var servicio = CrearServicio(handler);

        var resultado = await servicio.ReverseCityAsync(-16.409, -71.537);

        Assert.Equal("Arequipa", resultado);
        Assert.Equal(1, handler.Llamadas);
        Assert.Contains("SkillLinkApp/1.0", handler.UserAgent);
    }

    [Fact]
    public async Task ReverseCity_UsaDisplayNameComoRespaldo()
    {
        const string json = """
            { "display_name": "Trujillo, Provincia de Trujillo, La Libertad, Perú" }
            """;
        var handler = new StubHandler(_ => RespuestaJson(json));
        var servicio = CrearServicio(handler);

        var resultado = await servicio.ReverseCityAsync(-8.111, -79.029);

        Assert.Equal("La Libertad", resultado);
    }

    [Fact]
    public async Task ReverseCity_RespuestaHttpFallida_DevuelveNullSinRomper()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var servicio = CrearServicio(handler);

        var resultado = await servicio.ReverseCityAsync(-12.046, -77.0428);

        Assert.Null(resultado);
        Assert.Equal(1, handler.Llamadas);
    }

    [Fact]
    public async Task ReverseCity_JsonInvalido_DevuelveNullSinRomper()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{ esto no es json", Encoding.UTF8, "application/json")
        });
        var servicio = CrearServicio(handler);

        var resultado = await servicio.ReverseCityAsync(-12.047, -77.043);

        Assert.Null(resultado);
        Assert.Equal(1, handler.Llamadas);
    }

    [Fact]
    public async Task ReverseCity_ReutilizaCacheParaCoordenadaRedondeada()
    {
        const string json = """
            { "address": { "state": "Lima" } }
            """;
        var handler = new StubHandler(_ => RespuestaJson(json));
        var servicio = CrearServicio(handler);

        var primero = await servicio.ReverseCityAsync(-12.04641, -77.04281);
        var segundo = await servicio.ReverseCityAsync(-12.04644, -77.04284);

        Assert.Equal("Lima", primero);
        Assert.Equal("Lima", segundo);
        Assert.Equal(1, handler.Llamadas);
    }

    [Fact]
    public async Task ReverseCity_Deshabilitado_DevuelveNullSinLlamar()
    {
        var handler = new StubHandler(_ => RespuestaJson("{}"));
        var servicio = CrearServicio(handler, new GeoOptions
        {
            Enabled = false,
            BaseUrl = "https://nominatim.openstreetmap.org",
            UserAgent = "SkillLinkApp/1.0 (test@example.com)"
        });

        var resultado = await servicio.ReverseCityAsync(-12.046, -77.043);

        Assert.Null(resultado);
        Assert.Equal(0, handler.Llamadas);
    }

    private static GeoService CrearServicio(StubHandler handler, GeoOptions? options = null)
    {
        options ??= new GeoOptions
        {
            Enabled = true,
            BaseUrl = "https://nominatim.openstreetmap.org",
            UserAgent = "SkillLinkApp/1.0 (test@example.com)",
            CacheMinutes = 1440,
            TimeoutSeconds = 5
        };

        var http = new HttpClient(handler) { BaseAddress = new Uri(options.BaseUrl) };
        var cache = new MemoryCache(new MemoryCacheOptions());
        return new GeoService(http, Options.Create(options), cache, NullLogger<GeoService>.Instance);
    }

    private static HttpResponseMessage RespuestaJson(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int Llamadas { get; private set; }

        public string? UserAgent { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Llamadas++;
            UserAgent = request.Headers.UserAgent.ToString();
            return Task.FromResult(responder(request));
        }
    }
}
