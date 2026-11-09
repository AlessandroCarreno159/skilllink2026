using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace SkillLink_dotnet.Services.Chatbot;

public sealed record ChatRateResult(bool Allowed, string? Reason = null, int RetryAfterSeconds = 0);

public sealed class ChatRateLimiter(
    RedisConnectionProvider redis,
    IOptions<ChatbotOptions> options,
    ILogger<ChatRateLimiter> logger)
{
    private sealed class Contador
    {
        public int Count;
        public DateTime ExpiresAt;
    }

    private readonly ConcurrentDictionary<string, Contador> _local = new();

    public async Task<ChatRateResult> CheckAsync(string userId, CancellationToken ct = default)
    {
        var ahora = DateTime.UtcNow;
        var dia = ahora.ToString("yyyyMMdd");
        var minuto = ahora.ToString("yyyyMMddHHmm");
        var dailyKey = $"skilllink:chatbot:rate:daily:{userId}:{dia}";
        var burstKey = $"skilllink:chatbot:rate:burst:{userId}:{minuto}";
        var dailyLimit = Math.Max(1, options.Value.DailyMessages);
        var burstLimit = Math.Max(1, options.Value.BurstPerMinute);

        var db = redis.TryGetDatabase();
        if (db is not null)
        {
            try
            {
                var diario = await IncrementarAsync(db, dailyKey, hastaMedianoche(ahora));
                var rafaga = await IncrementarAsync(db, burstKey, TimeSpan.FromMinutes(2));

                if (rafaga > burstLimit)
                    return new ChatRateResult(false, "Demasiados mensajes seguidos. Espera un momento.", SegundosHastaSiguienteMinuto(ahora));

                if (diario > dailyLimit)
                    return new ChatRateResult(false, "Alcanzaste el límite diario del asistente. Podrás usarlo nuevamente mañana.", SegundosHastaMedianoche(ahora));

                return new ChatRateResult(true);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Rate limit del chatbot en Redis falló; se usará memoria local.");
            }
        }

        var localDiario = IncrementarLocal($"daily:{userId}:{dia}", dailyLimit, hastaMedianoche(ahora), ahora);
        var localRafaga = IncrementarLocal($"burst:{userId}:{minuto}", burstLimit, TimeSpan.FromMinutes(2), ahora);
        if (!localRafaga)
            return new ChatRateResult(false, "Demasiados mensajes seguidos. Espera un momento.", SegundosHastaSiguienteMinuto(ahora));
        if (!localDiario)
            return new ChatRateResult(false, "Alcanzaste el límite diario del asistente. Podrás usarlo nuevamente mañana.", SegundosHastaMedianoche(ahora));
        return new ChatRateResult(true);
    }

    private static async Task<long> IncrementarAsync(IDatabase db, string key, TimeSpan ttl)
    {
        var total = await db.StringIncrementAsync(key);
        if (total == 1) await db.KeyExpireAsync(key, ttl);
        return total;
    }

    private bool IncrementarLocal(string key, int limit, TimeSpan ttl, DateTime ahora)
    {
        var contador = _local.GetOrAdd(key, _ => new Contador { ExpiresAt = ahora.Add(ttl) });
        lock (contador)
        {
            if (contador.ExpiresAt <= ahora)
            {
                contador.Count = 0;
                contador.ExpiresAt = ahora.Add(ttl);
            }
            contador.Count++;
            if (_local.Count > 2000)
            {
                foreach (var viejo in _local.Where(x => x.Value.ExpiresAt <= ahora).Take(200).ToList())
                    _local.TryRemove(viejo.Key, out _);
            }
            return contador.Count <= limit;
        }
    }

    private static TimeSpan hastaMedianoche(DateTime utc)
        => utc.Date.AddDays(1) - utc;

    private static int SegundosHastaMedianoche(DateTime utc)
        => Math.Max(1, (int)(utc.Date.AddDays(1) - utc).TotalSeconds);

    private static int SegundosHastaSiguienteMinuto(DateTime utc)
    {
        var siguiente = utc.AddMinutes(1);
        siguiente = new DateTime(siguiente.Year, siguiente.Month, siguiente.Day, siguiente.Hour, siguiente.Minute, 0, DateTimeKind.Utc);
        return Math.Max(1, (int)(siguiente - utc).TotalSeconds);
    }
}
