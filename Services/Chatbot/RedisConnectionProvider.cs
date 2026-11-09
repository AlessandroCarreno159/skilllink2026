using StackExchange.Redis;

namespace SkillLink_dotnet.Services.Chatbot;

public sealed class RedisConnectionProvider : IDisposable
{
    private readonly IConfiguration _config;
    private readonly ILogger<RedisConnectionProvider> _logger;
    private readonly object _sync = new();
    private IConnectionMultiplexer? _multiplexer;
    private DateTime _nextRetryUtc = DateTime.MinValue;
    private bool _disposed;

    public RedisConnectionProvider(IConfiguration config, ILogger<RedisConnectionProvider> logger)
    {
        _config = config;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ResolveRedisUrl());

    public IDatabase? TryGetDatabase()
    {
        var multiplexer = GetMultiplexer();
        if (multiplexer is null) return null;
        try
        {
            return multiplexer.GetDatabase();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo obtener la base Redis para el chatbot.");
            return null;
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
                _nextRetryUtc = DateTime.UtcNow.AddSeconds(20);
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
                if (!multiplexer.IsConnected)
                {
                    _nextRetryUtc = DateTime.UtcNow.AddSeconds(20);
                    try { multiplexer.Dispose(); } catch { /* nada */ }
                    _logger.LogWarning("Redis no está disponible para el chatbot; se usará memoria local.");
                    return null;
                }

                _multiplexer = multiplexer;
                _logger.LogInformation("Redis conectado para el chatbot.");
                return _multiplexer;
            }
            catch (Exception ex)
            {
                _nextRetryUtc = DateTime.UtcNow.AddSeconds(20);
                _logger.LogWarning(ex, "No se pudo conectar a Redis para el chatbot; se usará memoria local.");
                return null;
            }
        }
    }

    private string? ResolveRedisUrl()
        => _config["REDIS_URL"]
           ?? _config.GetConnectionString("Redis")
           ?? _config["Redis:Url"]
           ?? _config["Redis:ConnectionString"];

    private static ConfigurationOptions BuildOptions(string url)
    {
        if (!url.StartsWith("redis://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("rediss://", StringComparison.OrdinalIgnoreCase))
        {
            return ConfigurationOptions.Parse(url);
        }

        var uri = new Uri(url);
        var options = new ConfigurationOptions
        {
            EndPoints = { { uri.Host, uri.IsDefaultPort ? 6379 : uri.Port } },
            Ssl = uri.Scheme.Equals("rediss", StringComparison.OrdinalIgnoreCase)
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
