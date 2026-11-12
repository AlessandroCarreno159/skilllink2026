using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SkillLink_dotnet.Services.Currency;

namespace SkillLink.Tests;

public sealed class ExchangeRateServiceTests
{
    [Fact]
    public async Task GetPenToUsdAsync_ParseaConversionRates()
    {
        var handler = new StubHandler("""
            {"result":"success","base_code":"PEN","conversion_rates":{"PEN":1,"USD":0.29}}
            """);
        var service = Crear(handler, new CurrencyOptions { ApiKey = "test" });

        var rate = await service.GetPenToUsdAsync();

        Assert.NotNull(rate);
        Assert.Equal(0.29m, rate.Value);
        Assert.Equal(1, handler.Llamadas);
    }

    [Fact]
    public async Task GetPenToUsdAsync_CacheaElExito()
    {
        var handler = new StubHandler("""
            {"result":"success","conversion_rates":{"USD":0.29}}
            """);
        var service = Crear(handler, new CurrencyOptions { ApiKey = "test" });

        await service.GetPenToUsdAsync();
        await service.GetPenToUsdAsync();

        Assert.Equal(1, handler.Llamadas);
    }

    [Fact]
    public async Task GetPenToUsdAsync_ErrorDeApi_UsaFallback()
    {
        var handler = new StubHandler("""
            {"result":"error","error-type":"quota"}
            """);
        var service = Crear(handler, new CurrencyOptions
        {
            ApiKey = "test",
            FallbackPenToUsd = 0.28m,
        });

        var rate = await service.GetPenToUsdAsync();

        Assert.NotNull(rate);
        Assert.Equal(0.28m, rate.Value);
    }

    [Fact]
    public async Task GetPenToUsdAsync_SinApiKey_NoLlamaAlServicio()
    {
        var handler = new StubHandler("{}");
        var service = Crear(handler, new CurrencyOptions { ApiKey = "" });

        var rate = await service.GetPenToUsdAsync();

        Assert.Null(rate);
        Assert.Equal(0, handler.Llamadas);
    }

    [Fact]
    public async Task ConvertAsync_ConvierteEnAmbasDirecciones()
    {
        var handler = new StubHandler("""
            {"result":"success","conversion_rates":{"USD":0.5}}
            """);
        var service = Crear(handler, new CurrencyOptions { ApiKey = "test" });

        var aUsd = await service.ConvertAsync(10m, "PEN", "USD");
        var aPen = await service.ConvertAsync(10m, "USD", "PEN");

        Assert.Equal(5m, aUsd);
        Assert.Equal(20m, aPen);
    }

    private static ExchangeRateService Crear(StubHandler handler, CurrencyOptions options)
    {
        var http = new HttpClient(handler);
        var cache = new MemoryCache(new MemoryCacheOptions());
        return new ExchangeRateService(
            http,
            cache,
            Options.Create(options),
            NullLogger<ExchangeRateService>.Instance);
    }

    private sealed class StubHandler(string json) : HttpMessageHandler
    {
        public int Llamadas { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Llamadas++;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }
}
