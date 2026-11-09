using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace SkillLink_dotnet.Services.Chatbot;

public sealed class ChatSessionStore(
    RedisConnectionProvider redis,
    IMemoryCache memoria,
    IOptions<ChatbotOptions> options,
    ILogger<ChatSessionStore> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ChatSession> GetAsync(string userId, CancellationToken ct = default)
    {
        var key = Key(userId);
        var db = redis.TryGetDatabase();
        if (db is not null)
        {
            try
            {
                var raw = await db.StringGetAsync(key);
                if (!raw.IsNullOrEmpty)
                {
                    var sesion = JsonSerializer.Deserialize<ChatSession>(raw.ToString(), JsonOptions);
                    if (sesion is not null) return sesion;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "No se pudo leer la sesión del chatbot en Redis.");
            }
        }

        if (memoria.TryGetValue<ChatSession>(key, out var local) && local is not null)
            return local;

        var nueva = new ChatSession(Guid.NewGuid().ToString("N"), new List<ChatSessionMessage>());
        await SaveAsync(userId, nueva, ct);
        return nueva;
    }

    public async Task SaveAsync(string userId, ChatSession sesion, CancellationToken ct = default)
    {
        var max = Math.Max(2, options.Value.MaxHistoryMessages);
        if (sesion.Messages.Count > max)
            sesion.Messages.RemoveRange(0, sesion.Messages.Count - max);

        var ttl = TimeSpan.FromMinutes(Math.Max(5, options.Value.SessionTtlMinutes));
        var key = Key(userId);
        var json = JsonSerializer.Serialize(sesion, JsonOptions);

        var db = redis.TryGetDatabase();
        if (db is not null)
        {
            try
            {
                await db.StringSetAsync(key, json, ttl);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "No se pudo guardar la sesión del chatbot en Redis.");
            }
        }

        memoria.Set(key, sesion, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl
        });
    }

    public async Task ClearAsync(string userId, CancellationToken ct = default)
    {
        var key = Key(userId);
        memoria.Remove(key);
        var db = redis.TryGetDatabase();
        if (db is null) return;
        try
        {
            await db.KeyDeleteAsync(key);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo borrar la sesión del chatbot en Redis.");
        }
    }

    private static string Key(string userId) => $"skilllink:chatbot:session:{userId}";
}
