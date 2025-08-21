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

// Load environment variables from .env file in development
if (File.Exists(".env"))
{
    Env.Load();
}

// Configure port for Railway deployment
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
Environment.SetEnvironmentVariable("ASPNETCORE_URLS", $"http://+:{port}");

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure Redis connection
var redisConnectionString = builder.Configuration["REDIS_CONNECTION_STRING"] 
    ?? builder.Configuration["REDIS_URL"]
    ?? builder.Configuration["REDIS_PRIVATE_URL"]
    ?? Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING") 
    ?? Environment.GetEnvironmentVariable("REDIS_URL")
    ?? Environment.GetEnvironmentVariable("REDIS_PRIVATE_URL");

// Fix duplicate ports in Redis URL if present
if (!string.IsNullOrEmpty(redisConnectionString))
{
    redisConnectionString = System.Text.RegularExpressions.Regex.Replace(
        redisConnectionString, 
        @":6379:\d+", 
        ":6379"
    );
}

// Always register memory cache (for rate limiting and fallback)
builder.Services.AddMemoryCache();

if (!string.IsNullOrEmpty(redisConnectionString))
{
    builder.Services.AddSingleton<IConnectionMultiplexer>(provider =>
    {
        try
        {
            var simpleConfig = $"redis.railway.internal:6379,password=jiJceFvaWIjdTAepEepOTppPdLOEEsCo,abortConnect=false,connectTimeout=20000,syncTimeout=15000";
            var configuration = ConfigurationOptions.Parse(simpleConfig);
            
            configuration.AbortOnConnectFail = false;
            configuration.ConnectTimeout = 20000;
            configuration.SyncTimeout = 15000;
            configuration.CommandMap = CommandMap.Create(new HashSet<string> 
            { 
                "INFO", "CONFIG", "CLUSTER", "PING", "ECHO", "CLIENT" 
            }, available: false);
            
            var multiplexer = ConnectionMultiplexer.Connect(configuration);
            return multiplexer;
        }
        catch
        {
            return null;
        }
    });
}
else
{
    builder.Services.AddSingleton<IConnectionMultiplexer>(provider => null);
}

// Configure AWS SES client
builder.Services.AddScoped<IAmazonSimpleEmailServiceV2>(provider =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    
    var awsAccessKey = configuration["AWS_ACCESS_KEY_ID"] ?? Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID");
    var awsSecretKey = configuration["AWS_SECRET_ACCESS_KEY"] ?? Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY");
    var awsRegion = configuration["AWS_DEFAULT_REGION"] ?? Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION") ?? "us-east-1";
    
    if (string.IsNullOrEmpty(awsAccessKey) || string.IsNullOrEmpty(awsSecretKey))
    {
        throw new InvalidOperationException("AWS credentials not found. Please set AWS_ACCESS_KEY_ID and AWS_SECRET_ACCESS_KEY environment variables.");
    }
    
    var credentials = new Amazon.Runtime.BasicAWSCredentials(awsAccessKey, awsSecretKey);
    var region = Amazon.RegionEndpoint.GetBySystemName(awsRegion);
    
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

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new { 
    status = "healthy", 
    timestamp = DateTime.UtcNow
}));

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

        return Results.Ok(new { message = "Magic link sent successfully" });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to send magic link");
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
        return Results.Redirect("/error.html");
    }

    try
    {
        // Validate token and get email
        var email = await tokenService.ValidateTokenAsync(token);
        if (string.IsNullOrEmpty(email))
        {
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

// Fallback to index.html for root path
app.MapFallback("/", () => Results.Redirect("/index.html"));

app.Run();

