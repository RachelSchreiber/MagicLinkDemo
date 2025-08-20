using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.Distributed;
using System.Text;

namespace MagicLinkDemo.Services;

public class TokenService
{
    private readonly IMemoryCache? _memoryCache;
    private readonly IDistributedCache? _distributedCache;
    private readonly bool _useRedis;

    public TokenService(IServiceProvider serviceProvider)
    {
        // Try to get Redis cache first, fallback to memory cache
        _distributedCache = serviceProvider.GetService<IDistributedCache>();
        _memoryCache = serviceProvider.GetService<IMemoryCache>();
        _useRedis = _distributedCache != null;
        
        Console.WriteLine(_useRedis ? "🔴 TokenService using Redis cache" : "💾 TokenService using Memory cache");
    }

    public async Task<string> GenerateSimpleTokenAsync(string email)
    {
        // Generate a simple random token
        var token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        var key = $"token:{token}";
        
        if (_useRedis && _distributedCache != null)
        {
            try
            {
                // Store in Redis for 15 minutes with retry logic
                var options = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15)
                };
                
                // Try with shorter timeout first
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await _distributedCache.SetStringAsync(key, email, options, cts.Token);
                Console.WriteLine($"✅ Token stored in Redis: {key}");
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"⚠️ Redis timeout after 3 seconds, falling back to memory cache");
                // Fallback to memory cache
                if (_memoryCache != null)
                {
                    _memoryCache.Set(key, email, TimeSpan.FromMinutes(15));
                    Console.WriteLine($"💾 Token stored in memory cache: {key}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Redis failed, falling back to memory cache: {ex.Message}");
                // Fallback to memory cache
                if (_memoryCache != null)
                {
                    _memoryCache.Set(key, email, TimeSpan.FromMinutes(15));
                    Console.WriteLine($"💾 Token stored in memory cache: {key}");
                }
            }
        }
        else if (_memoryCache != null)
        {
            // Store in memory cache for 15 minutes
            _memoryCache.Set(key, email, TimeSpan.FromMinutes(15));
        }
        
        return token;
    }

    public async Task<string?> ValidateTokenAsync(string token)
    {
        var key = $"token:{token}";
        
        if (_useRedis && _distributedCache != null)
        {
            try
            {
                // Get from Redis with timeout
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var email = await _distributedCache.GetStringAsync(key, cts.Token);
                if (email != null)
                {
                    // Remove token after use (one-time use)
                    await _distributedCache.RemoveAsync(key, cts.Token);
                    Console.WriteLine($"✅ Token validated from Redis: {key}");
                    return email;
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"⚠️ Redis validation timeout after 3 seconds, trying memory cache");
                // Fallback to memory cache
                if (_memoryCache != null && _memoryCache.TryGetValue(key, out var fallbackEmail))
                {
                    _memoryCache.Remove(key);
                    Console.WriteLine($"💾 Token validated from memory cache: {key}");
                    return fallbackEmail?.ToString();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Redis validation failed, trying memory cache: {ex.Message}");
                // Fallback to memory cache
                if (_memoryCache != null && _memoryCache.TryGetValue(key, out var fallbackEmail))
                {
                    _memoryCache.Remove(key);
                    Console.WriteLine($"💾 Token validated from memory cache: {key}");
                    return fallbackEmail?.ToString();
                }
            }
        }
        else if (_memoryCache != null)
        {
            // Get from memory cache
            if (_memoryCache.TryGetValue(key, out var email))
            {
                // Remove token after use (one-time use)
                _memoryCache.Remove(key);
                Console.WriteLine($"💾 Token validated from memory cache: {key}");
                return email?.ToString();
            }
        }
        
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
