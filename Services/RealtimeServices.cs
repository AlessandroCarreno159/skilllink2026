using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StackExchange.Redis;

namespace SkillLink_dotnet.Services;

/// <summary>
/// Publica eventos desde ASP.NET Core hacia el gateway Node/Socket.io usando
/// el canal Redis <c>skilllink:events</c>. Si Redis no está disponible, la app
/// sigue funcionando y solo se pierde el aviso en tiempo real.
/// </summary>
public sealed class RealtimePublisher : IDisposable
{
    public const string DefaultChannel = "skilllink:events";

    private readonly IConfiguration _config;
    private readonly ILogger<RealtimePublisher> _logger;
    private readonly object _sync = new();
    private IConnectionMultiplexer? _multiplexer;
    private DateTime _nextRetryUtc = DateTime.MinValue;
    private bool _disposed;

    public RealtimePublisher(IConfiguration config, ILogger<RealtimePublisher> logger)
    {
        _config = config;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ResolveRedisUrl());

    public async Task PublishAsync(string type, string userId, object payload, string? room = null)
    {
        var multiplexer = GetMultiplexer();
        if (multiplexer is null || !multiplexer.IsConnected) return;

        try
        {
            var message = JsonSerializer.Serialize(new
            {
                type,
                userId,
                room,
                payload,
                at = DateTimeOffset.UtcNow
            });
            var channel = _config["Realtime:Channel"] ?? DefaultChannel;
            await multiplexer.GetSubscriber().PublishAsync(RedisChannel.Literal(channel), message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo publicar el evento {Tipo} en Redis.", type);
        }
    }

    private IConnectionMultiplexer? GetMultiplexer()
    {
        lock (_sync)
        {
            if (_multiplexer is not null)
            {
                if (_multiplexer.IsConnected) return _multiplexer;
                if (DateTime.UtcNow < _nextRetryUtc) return null;
                _nextRetryUtc = DateTime.UtcNow.AddSeconds(15);
                try { _multiplexer.Dispose(); } catch { /* se reintenta luego */ }
                _multiplexer = null;
            }

            if (DateTime.UtcNow < _nextRetryUtc) return null;

            var url = ResolveRedisUrl();
            if (string.IsNullOrWhiteSpace(url)) return null;

            try
            {
                var options = BuildOptions(url);
                options.AbortOnConnectFail = false;
                options.ConnectRetry = 1;
                options.ConnectTimeout = 1500;
                options.AsyncTimeout = 1500;
                options.SyncTimeout = 1500;

                var multiplexer = ConnectionMultiplexer.Connect(options);
                try
                {
                    multiplexer.GetDatabase().Ping();
                }
                catch
                {
                    try { multiplexer.Dispose(); } catch { /* nada */ }
                    throw;
                }

                if (!multiplexer.IsConnected)
                {
                    _nextRetryUtc = DateTime.UtcNow.AddSeconds(15);
                    try { multiplexer.Dispose(); } catch { /* nada */ }
                    _logger.LogWarning("Redis no está disponible para eventos en tiempo real ({Url}).", url);
                    return null;
                }

                _multiplexer = multiplexer;
                _logger.LogInformation("Redis conectado para eventos en tiempo real.");
                return _multiplexer;
            }
            catch (Exception ex)
            {
                // Reintenta en 15s para no martillar Redis ni bloquear cada request.
                _nextRetryUtc = DateTime.UtcNow.AddSeconds(15);
                _logger.LogWarning(ex, "No se pudo conectar a Redis ({Url}); se reintentará luego.", MaskUrl(url));
                return null;
            }
        }
    }

    private static string? ResolveRedisUrl()
    {
        // Solo variables de entorno: nunca credenciales en appsettings ni en el repo.
        var url = Environment.GetEnvironmentVariable("REDIS_URL")
                  ?? Environment.GetEnvironmentVariable("ConnectionStrings__Redis");
        return string.IsNullOrWhiteSpace(url) ? null : url.Trim();
    }

    private static string MaskUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            if (string.IsNullOrEmpty(uri.UserInfo)) return url;
            var user = uri.UserInfo.Split(':', 2)[0];
            return new UriBuilder(uri) { UserName = user, Password = "***" }.Uri.ToString();
        }
        catch
        {
            return "redis";
        }
    }

    private static ConfigurationOptions BuildOptions(string url)
    {
        if (!url.StartsWith("redis://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("rediss://", StringComparison.OrdinalIgnoreCase))
        {
            return ConfigurationOptions.Parse(url);
        }

        var uri = new Uri(url);
        var useSsl = uri.Scheme.Equals("rediss", StringComparison.OrdinalIgnoreCase);
        var options = new ConfigurationOptions
        {
            EndPoints = { { uri.Host, uri.IsDefaultPort ? 6379 : uri.Port } },
            Ssl = useSsl,
            // Upstash usa TLS con SNI; fijar SslHost evita fallos de handshake.
            SslHost = useSsl ? uri.Host : null
        };

        if (!string.IsNullOrWhiteSpace(uri.UserInfo))
        {
            var parts = uri.UserInfo.Split(':', 2);
            if (parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]))
                options.User = Uri.UnescapeDataString(parts[0]);
            if (parts.Length > 1)
                options.Password = Uri.UnescapeDataString(parts[1]);
        }

        var database = uri.AbsolutePath.Trim('/');
        if (int.TryParse(database, out var db)) options.DefaultDatabase = db;

        return options;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_sync)
        {
            _multiplexer?.Dispose();
            _multiplexer = null;
        }
    }
}

/// <summary>
/// Genera tokens HMAC-SHA256 de corta duración para autenticar el handshake
/// de Socket.io. El gateway Node valida el mismo token con REALTIME_SECRET.
/// </summary>
public sealed class RealtimeTokenService(IConfiguration config)
{
    public bool IsEnabled => !string.IsNullOrWhiteSpace(config["Realtime:Secret"]);

    public string? CreateToken(string userId, string nombre, string rol, TimeSpan? ttl = null)
    {
        var secret = config["Realtime:Secret"];
        if (string.IsNullOrWhiteSpace(secret)) return null;

        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            sub = userId,
            name = nombre,
            role = rol,
            exp = DateTimeOffset.UtcNow.Add(ttl ?? TimeSpan.FromHours(8)).ToUnixTimeSeconds()
        });

        var body = Base64Url(payload);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signature = Base64Url(hmac.ComputeHash(Encoding.ASCII.GetBytes(body)));
        return $"{body}.{signature}";
    }

    private static string Base64Url(byte[] data)
        => Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
