using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SkillLink_dotnet.Services.Chatbot;

namespace SkillLink.Tests;

public sealed class ChatSessionStoreTests
{
    [Fact]
    public async Task GetAsync_ConservaConversacionYDevuelveCopias()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var redis = CrearRedis();
        var store = CrearStore(redis, cache, maxHistory: 4);

        var primera = await store.GetAsync("usuario-copia");
        var segunda = await store.GetAsync("usuario-copia");
        segunda.Messages.Add(new ChatSessionMessage("user", "mensaje local", DateTimeOffset.UtcNow));

        var tercera = await store.GetAsync("usuario-copia");

        Assert.Equal(primera.ConversationId, tercera.ConversationId);
        Assert.Empty(tercera.Messages);
    }

    [Fact]
    public async Task SaveAsync_RecortaHistorial()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var redis = CrearRedis();
        var store = CrearStore(redis, cache, maxHistory: 4);
        var sesion = await store.GetAsync("usuario-historial");
        for (var i = 0; i < 6; i++)
            sesion.Messages.Add(new ChatSessionMessage("user", $"mensaje {i}", DateTimeOffset.UtcNow));

        await store.SaveAsync("usuario-historial", sesion);
        var recuperada = await store.GetAsync("usuario-historial");

        Assert.Equal(4, recuperada.Messages.Count);
        Assert.Equal("mensaje 2", recuperada.Messages[0].Text);
    }

    [Fact]
    public async Task ClearAsync_EliminaLaSesion()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var redis = CrearRedis();
        var store = CrearStore(redis, cache, maxHistory: 4);
        var anterior = await store.GetAsync("usuario-clear");
        anterior.Messages.Add(new ChatSessionMessage("user", "hola", DateTimeOffset.UtcNow));
        await store.SaveAsync("usuario-clear", anterior);

        await store.ClearAsync("usuario-clear");
        var nueva = await store.GetAsync("usuario-clear");

        Assert.Empty(nueva.Messages);
        Assert.NotEqual(anterior.ConversationId, nueva.ConversationId);
    }

    private static RedisConnectionProvider CrearRedis()
    {
        var configuracion = new ConfigurationBuilder().Build();
        return new RedisConnectionProvider(configuracion, NullLogger<RedisConnectionProvider>.Instance);
    }

    private static ChatSessionStore CrearStore(RedisConnectionProvider redis, IMemoryCache cache, int maxHistory)
    {
        var opciones = Options.Create(new ChatbotOptions
        {
            MaxHistoryMessages = maxHistory,
            SessionTtlMinutes = 5,
        });
        return new ChatSessionStore(redis, cache, opciones, NullLogger<ChatSessionStore>.Instance);
    }
}
