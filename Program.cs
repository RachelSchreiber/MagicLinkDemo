using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Amazon.SimpleEmailV2;
using Amazon.Runtime;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.Distributed;
using MagicLinkDemo.Models;
using MagicLinkDemo.Services;
using DotNetEnv;
using StackExchange.Redis;

// Load environment variables from .env file only in development
// Railway provides environment variables directly, so this is optional
if (File.Exists(".env"))
{
    try
    {
        Env.Load();
        Console.WriteLine("✅ .env file loaded successfully");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"⚠️ Could not load .env file: {ex.Message}");
    }
}
else
{
    Console.WriteLine("ℹ️ No .env file found (normal for Railway deployment)");
}

// For Railway deployment - use PORT environment variable if available, otherwise default to 8080
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
Environment.SetEnvironmentVariable("ASPNETCORE_URLS", $"http://+:{port}");
Console.WriteLine($"🚀 Application will listen on port {port}");

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure Redis connection string
var redisConnectionString = builder.Configuration["REDIS_CONNECTION_STRING"] 
    ?? builder.Configuration["REDIS_URL"] // Railway uses REDIS_URL
    ?? builder.Configuration["REDIS_PRIVATE_URL"] // Alternative Railway Redis variable
    ?? Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING") 
    ?? Environment.GetEnvironmentVariable("REDIS_URL")
    ?? Environment.GetEnvironmentVariable("REDIS_PRIVATE_URL");

if (!string.IsNullOrEmpty(redisConnectionString) && redisConnectionString.Contains(":6379:6379"))
{
    redisConnectionString = redisConnectionString.Replace(":6379:6379", ":6379");
    Console.WriteLine($"🔧 Fixed duplicate port in Redis URL");
}

Console.WriteLine($"🔍 Redis Connection String: {(string.IsNullOrEmpty(redisConnectionString) ? "❌ NOT SET" : "✅ SET")}");

// Always register memory cache (for rate limiting and fallback)
builder.Services.AddMemoryCache();

if (!string.IsNullOrEmpty(redisConnectionString))
{
    builder.Services.AddSingleton<IConnectionMultiplexer>(provider =>
    {
        try
        {
            Console.WriteLine($"🔍 Redis URL format: {redisConnectionString}");

            var configuration = ConfigurationOptions.Parse(redisConnectionString);
            configuration.AbortOnConnectFail = false; // Don't abort on connection failure
            configuration.ConnectTimeout = 5000; // 5 seconds
            configuration.SyncTimeout = 5000; // 5 seconds
            configuration.Ssl = true; // Enable SSL
            var multiplexer = ConnectionMultiplexer.Connect(configuration);
            Console.WriteLine($"✅ Redis ConnectionMultiplexer configured successfully");
            
            // Test the connection immediately
            var db = multiplexer.GetDatabase();
            db.Ping();
            Console.WriteLine($"✅ Redis ping test successful");
            
            return multiplexer;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Redis ConnectionMultiplexer failed to initialize: {ex.Message}");
            // Return a disconnected multiplexer that will cause graceful fallback
            return ConnectionMultiplexer.Connect("localhost:6379,abortConnect=false");
        }
    });
}

else
{
    // Register a dummy ConnectionMultiplexer that will fail gracefully
    builder.Services.AddSingleton<IConnectionMultiplexer>(provider =>
    {
        Console.WriteLine("⚠️ Redis not configured, using dummy ConnectionMultiplexer for fallback");
        return ConnectionMultiplexer.Connect("localhost:6379,abortConnect=false");
    });
}

// Configure AWS SES client with credentials and region
builder.Services.AddScoped<IAmazonSimpleEmailServiceV2>(provider =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    
    // Try IConfiguration first (Railway), then Environment variables (local)
    var awsAccessKey = configuration["AWS_ACCESS_KEY_ID"] ?? Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID");
    var awsSecretKey = configuration["AWS_SECRET_ACCESS_KEY"] ?? Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY");
    var awsRegion = configuration["AWS_DEFAULT_REGION"] ?? Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION") ?? "us-east-1";
    
    Console.WriteLine($"🔍 Checking AWS credentials...");
    Console.WriteLine($"AWS_ACCESS_KEY_ID: {(string.IsNullOrEmpty(awsAccessKey) ? "❌ NOT SET" : "✅ SET")}");
    Console.WriteLine($"AWS_SECRET_ACCESS_KEY: {(string.IsNullOrEmpty(awsSecretKey) ? "❌ NOT SET" : "✅ SET")}");
    Console.WriteLine($"AWS_DEFAULT_REGION: {awsRegion}");
    
    if (string.IsNullOrEmpty(awsAccessKey) || string.IsNullOrEmpty(awsSecretKey))
    {
        throw new InvalidOperationException("AWS credentials not found. Please set AWS_ACCESS_KEY_ID and AWS_SECRET_ACCESS_KEY environment variables in Railway.");
    }
    
    var credentials = new Amazon.Runtime.BasicAWSCredentials(awsAccessKey, awsSecretKey);
    var region = Amazon.RegionEndpoint.GetBySystemName(awsRegion);
    
    Console.WriteLine($"✅ AWS SES client configured for region: {awsRegion}");
    return new AmazonSimpleEmailServiceV2Client(credentials, region);
});

// Add custom services
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<EmailSender>();

// Configure Cookie Authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/";
        options.LogoutPath = "/auth/logout";
        options.AccessDeniedPath = "/error.html";
        options.Cookie.Name = "MagicLinkAuth";
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// Log environment information
Console.WriteLine($"🌍 Environment: {app.Environment.EnvironmentName}");
Console.WriteLine($"📧 SES_FROM_ADDRESS: {(string.IsNullOrEmpty(builder.Configuration["SES_FROM_ADDRESS"] ?? Environment.GetEnvironmentVariable("SES_FROM_ADDRESS")) ? "❌ NOT SET" : "✅ SET")}");
Console.WriteLine($"🔐 MAGICLINK_SECRET: {(string.IsNullOrEmpty(builder.Configuration["MAGICLINK_SECRET"] ?? Environment.GetEnvironmentVariable("MAGICLINK_SECRET")) ? "❌ NOT SET" : "✅ SET")}");

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Enable static files middleware
app.UseStaticFiles();

// Enable authentication and authorization
app.UseAuthentication();
app.UseAuthorization();

// Health check endpoint with environment variables status
app.MapGet("/health", (IConfiguration config) => {
    var envStatus = new {
        AWS_ACCESS_KEY_ID = !string.IsNullOrEmpty(config["AWS_ACCESS_KEY_ID"] ?? Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID")),
        AWS_SECRET_ACCESS_KEY = !string.IsNullOrEmpty(config["AWS_SECRET_ACCESS_KEY"] ?? Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY")),
        AWS_DEFAULT_REGION = config["AWS_DEFAULT_REGION"] ?? Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION") ?? "us-east-1",
        SES_FROM_ADDRESS = !string.IsNullOrEmpty(config["SES_FROM_ADDRESS"] ?? Environment.GetEnvironmentVariable("SES_FROM_ADDRESS")),
        MAGICLINK_SECRET = !string.IsNullOrEmpty(config["MAGICLINK_SECRET"] ?? Environment.GetEnvironmentVariable("MAGICLINK_SECRET")),
        REDIS_CONFIGURED = !string.IsNullOrEmpty(config["REDIS_CONNECTION_STRING"] ?? config["REDIS_URL"] ?? config["REDIS_PRIVATE_URL"] ??
                          Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING") ?? Environment.GetEnvironmentVariable("REDIS_URL") ??
                          Environment.GetEnvironmentVariable("REDIS_PRIVATE_URL"))
    };
    
    return Results.Ok(new { 
        status = "healthy", 
        timestamp = DateTime.UtcNow,
        environment = app.Environment.EnvironmentName,
        port = port,
        environmentVariables = envStatus
    });
});

// POST /auth/magic-link endpoint
app.MapPost("/auth/magic-link", async ([FromBody] MagicLinkRequest request,
    HttpContext context,
    IMemoryCache cache,
    TokenService tokenService,
    EmailSender emailSender,
    ILogger<Program> logger) =>
{
    // Validate request
    if (string.IsNullOrWhiteSpace(request.Email))
    {
        return Results.BadRequest(new { message = "Email is required" });
    }

    var emailAttribute = new EmailAddressAttribute();
    if (!emailAttribute.IsValid(request.Email))
    {
        return Results.BadRequest(new { message = "Invalid email format" });
    }

    // Rate limiting - per IP and per email (60 seconds)
    var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    var rateLimitKeyIp = $"rate_limit_ip:{clientIp}";
    var rateLimitKeyEmail = $"rate_limit_email:{request.Email.ToLowerInvariant()}";

    if (cache.TryGetValue(rateLimitKeyIp, out _) || cache.TryGetValue(rateLimitKeyEmail, out _))
    {
        return Results.StatusCode(429); // Too Many Requests
    }

    try
    {
        // Generate simple token
        var token = await tokenService.GenerateSimpleTokenAsync(request.Email);

        // Create magic link
        var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
        var magicLink = $"{baseUrl}/auth/callback?token={Uri.EscapeDataString(token)}";

        // Send email
        await emailSender.SendMagicLinkAsync(request.Email, magicLink);

        // Set rate limiting cache entries
        cache.Set(rateLimitKeyIp, true, TimeSpan.FromSeconds(60));
        cache.Set(rateLimitKeyEmail, true, TimeSpan.FromSeconds(60));

        logger.LogInformation("Magic link sent to {Email} from IP {ClientIP}", request.Email, clientIp);

        return Results.Ok(new { message = "Magic link sent successfully" });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to send magic link to {Email}", request.Email);
        return Results.Problem("Failed to send magic link. Please try again later.");
    }
});

// GET /auth/callback endpoint
app.MapGet("/auth/callback", async ([FromQuery] string? token,
    HttpContext context,
    TokenService tokenService,
    ILogger<Program> logger) =>
{
    if (string.IsNullOrWhiteSpace(token))
    {
        logger.LogWarning("Callback accessed without token");
        return Results.Redirect("/error.html");
    }

    try
    {
        // Validate simple token and get email
        var email = await tokenService.ValidateTokenAsync(token);
        if (string.IsNullOrEmpty(email))
        {
            logger.LogWarning("Invalid token used in callback");
            return Results.Redirect("/error.html");
        }

        // Create simple claims for the user
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Email, email),
            new Claim(ClaimTypes.Name, email),
            new Claim("LoginTime", DateTime.UtcNow.ToString())
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        // Sign in user with cookie authentication
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        logger.LogInformation("User {Email} successfully authenticated via magic link", email);

        return Results.Redirect("/success.html");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error during authentication callback");
        return Results.Redirect("/error.html");
    }
});

// GET /api/me endpoint (requires authentication)
app.MapGet("/api/me", [Authorize] (ClaimsPrincipal user) =>
{
    var email = user.FindFirst(ClaimTypes.Email)?.Value;
    var loginTime = user.FindFirst("LoginTime")?.Value;
    
    return Results.Ok(new 
    { 
        email = email,
        authenticated = true,
        loginTime = loginTime
    });
});

// Optional: Logout endpoint
app.MapPost("/auth/logout", [Authorize] async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Ok(new { message = "Logged out successfully" });
});

// Fallback to index.html for root path
app.MapFallback("/", () => Results.Redirect("/index.html"));

app.Run();

