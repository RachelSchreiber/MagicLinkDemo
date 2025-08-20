using Microsoft.Extensions.Caching.Memory;
using StackExchange.Redis;
using System.Text;

namespace MagicLinkDemo.Services;

public class TokenService
{
    private readonly ConnectionMultiplexer _redis;
    private readonly IMemoryCache _memoryCache;
    private readonly IConfiguration _configuration;
    private readonly IDatabase? _database;
    private readonly bool _redisAvailable;

    public TokenService(ConnectionMultiplexer redis, IMemoryCache memoryCache, IConfiguration configuration)
    {
        _redis = redis;
        _memoryCache = memoryCache;
        _configuration = configuration;
        
        try
        {
            if (_redis.IsConnected)
            {
                _database = _redis.GetDatabase();
                _redisAvailable = true;
                Console.WriteLine("🔴 TokenService using Redis cache");
            }
            else
            {
                _redisAvailable = false;
                Console.WriteLine("� TokenService using Memory cache (Redis not connected)");
            }
        }
        catch (Exception ex)
        {
            _redisAvailable = false;
            Console.WriteLine($"💾 TokenService using Memory cache (Redis error: {ex.Message})");
        }
    }

    public async Task<string> GenerateSimpleTokenAsync(string email)
    {
        // Generate a simple random token
        var token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        var key = $"token:{token}";
        
        if (_redisAvailable && _database != null)
        {
            try
            {
                // Store in Redis for 15 minutes
                var expiration = TimeSpan.FromMinutes(15);
                await _database.StringSetAsync(key, email, expiration);
                Console.WriteLine($"✅ Token stored in Redis: {key}");
                return token;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Redis failed, falling back to memory cache: {ex.Message}");
                // Fallback to memory cache
                _memoryCache.Set(key, email, TimeSpan.FromMinutes(15));
                Console.WriteLine($"💾 Token stored in memory cache: {key}");
                return token;
            }
        }
        else
        {
            // Store in memory cache for 15 minutes
            _memoryCache.Set(key, email, TimeSpan.FromMinutes(15));
            Console.WriteLine($"💾 Token stored in memory cache: {key}");
            return token;
        }
    }

    public async Task<string?> ValidateTokenAsync(string token)
    {
        var key = $"token:{token}";
        
        if (_redisAvailable && _database != null)
        {
            try
            {
                // Get from Redis
                var email = await _database.StringGetAsync(key);
                if (email.HasValue)
                {
                    // Remove token after use (one-time use)
                    await _database.KeyDeleteAsync(key);
                    Console.WriteLine($"✅ Token validated and removed from Redis: {key}");
                    return email.ToString();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Redis validation failed, trying memory cache: {ex.Message}");
                // Fallback to memory cache
                if (_memoryCache.TryGetValue(key, out var fallbackEmail))
                {
                    _memoryCache.Remove(key);
                    Console.WriteLine($"💾 Token validated and removed from memory cache: {key}");
                    return fallbackEmail?.ToString();
                }
            }
        }
        else
        {
            // Get from memory cache
            if (_memoryCache.TryGetValue(key, out var email))
            {
                // Remove token after use (one-time use)
                _memoryCache.Remove(key);
                Console.WriteLine($"💾 Token validated and removed from memory cache: {key}");
                return email?.ToString();
            }
        }
        
        Console.WriteLine($"❌ Token not found or expired: {key}");
        return null;
    }

    // Backward compatibility methods (synchronous)
    public string GenerateSimpleToken(string email)
    {
        return GenerateSimpleTokenAsync(email).GetAwaiter().GetResult();
    }

    public string? ValidateToken(string token)
    {
        return ValidateTokenAsync(token).GetAwaiter().GetResult();
    }
}
