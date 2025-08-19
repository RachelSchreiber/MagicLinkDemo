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

// Load environment variables from .env file
Env.Load();

// For AWS deployment - ensure the app listens on port 8080
Environment.SetEnvironmentVariable("ASPNETCORE_URLS", "http://+:8080");

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure caching - Railway Redis connection  
var redisConnectionString = Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING") 
    ?? Environment.GetEnvironmentVariable("REDIS_URL"); // Railway uses REDIS_URL

// Always register memory cache (for rate limiting and fallback)
builder.Services.AddMemoryCache();

if (!string.IsNullOrEmpty(redisConnectionString))
{
    try
    {
        // Railway/Redis connection
        builder.Services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisConnectionString;
            options.InstanceName = "MagicLinkDemo";
        });
        Console.WriteLine($"✅ Redis configured: {redisConnectionString}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"⚠️ Redis connection failed: {ex.Message}");
        Console.WriteLine("🔄 Fallback to in-memory cache");
    }
}
else
{
    Console.WriteLine("⚠️ Using in-memory cache (Redis not configured)");
}

// Configure AWS SES client with credentials and region
builder.Services.AddScoped<IAmazonSimpleEmailServiceV2>(provider =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    
    var awsAccessKey = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID");
    var awsSecretKey = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY");
    var awsRegion = Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION") ?? "us-east-1";
    
    if (string.IsNullOrEmpty(awsAccessKey) || string.IsNullOrEmpty(awsSecretKey))
    {
        throw new InvalidOperationException("AWS credentials not found. Please check your .env file.");
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
    timestamp = DateTime.UtcNow,
    environment = app.Environment.EnvironmentName
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
        var token = tokenService.GenerateSimpleToken(request.Email);

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
        var email = tokenService.ValidateToken(token);
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

