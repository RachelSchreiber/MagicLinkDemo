using Microsoft.Extensions.Caching.Memory;
using StackExchange.Redis;

namespace MagicLinkDemo.Services;

public class TokenService
{
    private readonly IConnectionMultiplexer? _redis;
    private readonly IMemoryCache _memoryCache;

    public TokenService(IConnectionMultiplexer? redis, IMemoryCache memoryCache)
    {
        _redis = redis;
        _memoryCache = memoryCache;
    }

    private bool TryGetRedis(out IDatabase? db)
    {
        if (_redis != null && _redis.IsConnected)
        {
            try
            {
                db = _redis.GetDatabase();
                // Test the connection with a simple ping
                var pingResult = db.Ping();
                Console.WriteLine($"🔍 Redis ping: {pingResult.TotalMilliseconds}ms");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Redis connection test failed: {ex.Message}");
                db = null;
                return false;
            }
        }

        Console.WriteLine($"⚠️ Redis is null or disconnected");
        db = null;
        return false;
    }


public async Task<string> GenerateSimpleTokenAsync(string email)
{
    var token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
    var key = $"token:{token}";

    Console.WriteLine($"🔍 Attempting to use Redis for token storage...");
    
    if (TryGetRedis(out var db) && db != null)
    {
        try
        {
            await db.StringSetAsync(key, email, TimeSpan.FromMinutes(15));
            Console.WriteLine($"✅ Token stored in Redis: {key}");
            return token;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Redis failed, fallback to memory: {ex.Message}");
        }
    }
    else
    {
        Console.WriteLine($"⚠️ Redis not available, using memory cache");
    }

    _memoryCache.Set(key, email, TimeSpan.FromMinutes(15));
    Console.WriteLine($"💾 Token stored in memory: {key}");
    return token;
}

    public async Task<string?> ValidateTokenAsync(string token)
    {
        var key = $"token:{token}";

        if (TryGetRedis(out var db) && db != null)
        {
            try
            {
                var email = await db.StringGetAsync(key);
                if (email.HasValue)
                {
                    await db.KeyDeleteAsync(key);
                    Console.WriteLine($"✅ Token validated and removed from Redis: {key}");
                    return email.ToString();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Redis validation failed, trying memory: {ex.Message}");
            }
        }

        if (_memoryCache.TryGetValue(key, out var fallbackEmail))
        {
            _memoryCache.Remove(key);
            Console.WriteLine($"💾 Token validated and removed from memory: {key}");
            return fallbackEmail?.ToString();
        }

        Console.WriteLine($"❌ Token not found or expired: {key}");
        return null;
    }
}
