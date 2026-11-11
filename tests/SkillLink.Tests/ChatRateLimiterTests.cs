using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SkillLink_dotnet.Services.Chatbot;

namespace SkillLink.Tests;

public sealed class ChatRateLimiterTests
{
    [Fact]
    public async Task CheckAsync_BloqueaRafagaExcedida()
    {
        using var redis = CrearRedis();
        var limiter = CrearLimiter(redis, burst: 1, daily: 10);

        Assert.True((await limiter.CheckAsync("usuario-rafaga")).Allowed);

        var bloqueado = await limiter.CheckAsync("usuario-rafaga");

        Assert.False(bloqueado.Allowed);
        Assert.Contains("seguidos", bloqueado.Reason);
    }

    [Fact]
    public async Task CheckAsync_BloqueaLimiteDiario()
    {
        using var redis = CrearRedis();
        var limiter = CrearLimiter(redis, burst: 10, daily: 2);

        Assert.True((await limiter.CheckAsync("usuario-diario")).Allowed);
        Assert.True((await limiter.CheckAsync("usuario-diario")).Allowed);

        var bloqueado = await limiter.CheckAsync("usuario-diario");

        Assert.False(bloqueado.Allowed);
        Assert.Contains("diario", bloqueado.Reason);
    }

    [Fact]
    public async Task CheckAsync_AislaUsuarios()
    {
        using var redis = CrearRedis();
        var limiter = CrearLimiter(redis, burst: 1, daily: 10);

        Assert.True((await limiter.CheckAsync("usuario-a")).Allowed);
        Assert.True((await limiter.CheckAsync("usuario-b")).Allowed);
        Assert.False((await limiter.CheckAsync("usuario-a")).Allowed);
    }

    private static RedisConnectionProvider CrearRedis()
    {
        var configuracion = new ConfigurationBuilder().Build();
        return new RedisConnectionProvider(configuracion, NullLogger<RedisConnectionProvider>.Instance);
    }

    private static ChatRateLimiter CrearLimiter(RedisConnectionProvider redis, int burst, int daily)
    {
        var opciones = Options.Create(new ChatbotOptions
        {
            BurstPerMinute = burst,
            DailyMessages = daily,
        });
        return new ChatRateLimiter(redis, opciones, NullLogger<ChatRateLimiter>.Instance);
    }
}
