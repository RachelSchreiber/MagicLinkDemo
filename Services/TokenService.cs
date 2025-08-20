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
                db.Ping();
                return true;
            }
            catch
            {
                db = null;
                return false;
            }
        }

        db = null;
        return false;
    }


public async Task<string> GenerateSimpleTokenAsync(string email)
{
    var token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
    var key = $"token:{token}";
    
    if (TryGetRedis(out var db) && db != null)
    {
        try
        {
            await db.StringSetAsync(key, email, TimeSpan.FromMinutes(15));
            return token;
        }
        catch
        {
            // Fall back to memory cache
        }
    }

    _memoryCache.Set(key, email, TimeSpan.FromMinutes(15));
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
                    return email.ToString();
                }
            }
            catch
            {
                // Fall back to memory cache
            }
        }

        if (_memoryCache.TryGetValue(key, out var fallbackEmail))
        {
            _memoryCache.Remove(key);
            return fallbackEmail?.ToString();
        }

        return null;
    }
}
