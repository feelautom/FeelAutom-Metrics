using FeelAutoMetrics.Infrastructure.Data;
using FeelAutoMetrics.Infrastructure.Services;
using FeelAutoMetrics.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Globalization;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddOpenApi();

// DB Configuration
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
dataSourceBuilder.EnableDynamicJson();
var dataSource = dataSourceBuilder.Build();

builder.Services.AddPooledDbContextFactory<AppDbContext>(options =>
    options.UseNpgsql(dataSource, b => b.MigrationsAssembly("FeelAutoMetrics.Ingestor")));

// Enrichment Services
builder.Services.AddSingleton<IGeoIpService>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var dbPath = config["GeoIP:DatabasePath"];
    return new GeoIpService(dbPath);
});
builder.Services.AddSingleton<IUserAgentService, UserAgentService>();
builder.Services.AddSingleton<ISecurityService, SecurityService>();
builder.Services.AddScoped<ILogProcessor, LogProcessor>();

// Log Retention Background Service
builder.Services.AddHostedService<LogRetentionService>();

// AI Analyst Background Service (Gemini)
builder.Services.AddHostedService<AiAnalystService>();

var app = builder.Build();

// Auto-migrate on startup
{
    var factory = app.Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
    using var db = await factory.CreateDbContextAsync();
    await db.Database.MigrateAsync();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Pas de UseHttpsRedirection() : Traefik gère le TLS en amont

// API Key middleware : protège les endpoints /api/*
var apiKey = app.Configuration["Security:ApiKey"];
if (!string.IsNullOrEmpty(apiKey))
{
    app.Use(async (context, next) =>
    {
        var path = context.Request.Path.Value ?? "";
        if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            var providedKey = context.Request.Headers["X-Api-Key"].FirstOrDefault()
                ?? context.Request.Query["apikey"].FirstOrDefault();
            if (providedKey != apiKey)
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("Unauthorized: Invalid API key.");
                return;
            }
        }
        await next();
    });
}

// === IP Ban Check (ForwardAuth pour Traefik) ===
// Cache en mémoire des IPs bannies, refresh toutes les 30s
var _bannedCache = new HashSet<string>();
var _bannedCacheExpiry = DateTimeOffset.MinValue;
var _bannedCacheLock = new object();

async Task<HashSet<string>> GetBannedIpsAsync(IDbContextFactory<AppDbContext> factory)
{
    if (DateTimeOffset.UtcNow < _bannedCacheExpiry)
        lock (_bannedCacheLock) return _bannedCache;

    using var db = await factory.CreateDbContextAsync();
    var ips = await db.BannedIps.Where(b => b.IsActive).Select(b => b.IpAddress).ToListAsync();
    var set = ips.ToHashSet();
    lock (_bannedCacheLock)
    {
        _bannedCache = set;
        _bannedCacheExpiry = DateTimeOffset.UtcNow.AddSeconds(30);
    }
    return set;
}

// Endpoint ForwardAuth : Traefik appelle cet endpoint avant chaque requête
// Retourne 200 si l'IP est autorisée, 403 si bannie
app.MapGet("/api/security/check", async (HttpContext ctx, [FromServices] IDbContextFactory<AppDbContext> dbFactory) =>
{
    // Traefik envoie l'IP originale dans X-Forwarded-For
    var clientIp = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',').First().Trim()
        ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "";

    var banned = await GetBannedIpsAsync(dbFactory);
    if (banned.Contains(clientIp))
    {
        return Results.StatusCode(403);
    }
    return Results.Ok();
}).WithName("IpBanCheck");

// Root endpoint for health check
app.MapGet("/", () => Results.Ok(new
{
    Name = "FeelAuto-Metrics Ingestor",
    Status = "Online",
    Time = DateTimeOffset.UtcNow
}))
.WithName("HealthCheck");

// Ingestion Endpoint (Traefik Logs)
app.MapPost("/api/logs/ingest", async ([FromBody] object rawLog, [FromServices] ILogProcessor processor, [FromServices] IDbContextFactory<AppDbContext> dbFactory) =>
{
    try
    {
        var rawJson = rawLog.ToString();
        if (string.IsNullOrEmpty(rawJson)) return Results.BadRequest("Empty log body.");

        var log = await processor.ProcessAsync(rawJson);
        using var db = await dbFactory.CreateDbContextAsync();
        db.GlobalAccessLogs.Add(log);
        await db.SaveChangesAsync();

        return Results.Ok(new { id = log.Id });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
})
.WithName("IngestLog");

// Ingestion Endpoint (App Events)
app.MapPost("/api/events", async ([FromBody] AppEvent appEvent, [FromServices] IDbContextFactory<AppDbContext> dbFactory) =>
{
    try
    {
        if (appEvent.Timestamp == default) appEvent.Timestamp = DateTimeOffset.UtcNow;

        using var db = await dbFactory.CreateDbContextAsync();
        db.AppEvents.Add(appEvent);
        await db.SaveChangesAsync();

        return Results.Ok(new { id = appEvent.Id });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
})
.WithName("IngestEvent");

// === Export Endpoints ===

string EscapeCsv(string? value)
{
    if (string.IsNullOrEmpty(value)) return "";
    if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        return $"\"{value.Replace("\"", "\"\"")}\"";
    return value;
}

app.MapGet("/api/export/logs", async (
    [FromQuery] string? format,
    [FromQuery] string? domain,
    [FromQuery] string? from,
    [FromQuery] string? to,
    [FromServices] IDbContextFactory<AppDbContext> dbFactory) =>
{
    var fmt = (format ?? "json").ToLowerInvariant();

    using var db = await dbFactory.CreateDbContextAsync();
    IQueryable<GlobalAccessLog> query = db.GlobalAccessLogs;

    if (!string.IsNullOrEmpty(domain))
        query = query.Where(l => l.RequestHost == domain);

    if (DateTimeOffset.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var fromDate))
        query = query.Where(l => l.Timestamp >= fromDate);

    if (DateTimeOffset.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var toDate))
        query = query.Where(l => l.Timestamp <= toDate.AddDays(1));

    var logs = await query.OrderByDescending(l => l.Timestamp).Take(50000).ToListAsync();

    if (fmt == "csv")
    {
        var sb = new StringBuilder();
        sb.AppendLine("Id,Timestamp,ClientHost,RequestMethod,RequestPath,RequestHost,ResponseStatusCode,ResponseContentSize,DurationMs,CountryCode,CountryName,CityName,BrowserName,OsName,IsBot,BotCategory");
        foreach (var l in logs)
        {
            sb.AppendLine($"{l.Id},{l.Timestamp:O},{EscapeCsv(l.ClientHost)},{l.RequestMethod},{EscapeCsv(l.RequestPath)},{EscapeCsv(l.RequestHost)},{l.ResponseStatusCode},{l.ResponseContentSize},{l.DurationMs},{l.CountryCode},{EscapeCsv(l.CountryName)},{EscapeCsv(l.CityName)},{EscapeCsv(l.BrowserName)},{EscapeCsv(l.OsName)},{l.IsBot},{l.BotCategory}");
        }
        return Results.Text(sb.ToString(), "text/csv", Encoding.UTF8);
    }

    return Results.Json(logs);
})
.WithName("ExportLogs");

app.MapGet("/api/export/events", async (
    [FromQuery] string? format,
    [FromQuery] string? from,
    [FromQuery] string? to,
    [FromServices] IDbContextFactory<AppDbContext> dbFactory) =>
{
    var fmt = (format ?? "json").ToLowerInvariant();

    using var db = await dbFactory.CreateDbContextAsync();
    IQueryable<AppEvent> query = db.AppEvents;

    if (DateTimeOffset.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var fromDate))
        query = query.Where(e => e.Timestamp >= fromDate);

    if (DateTimeOffset.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var toDate))
        query = query.Where(e => e.Timestamp <= toDate.AddDays(1));

    var events = await query.OrderByDescending(e => e.Timestamp).Take(50000).ToListAsync();

    if (fmt == "csv")
    {
        var sb = new StringBuilder();
        sb.AppendLine("Id,Timestamp,AppName,Environment,Level,Message,Category,CorrelationId,UserId,ExceptionMessage");
        foreach (var e in events)
        {
            sb.AppendLine($"{e.Id},{e.Timestamp:O},{EscapeCsv(e.AppName)},{EscapeCsv(e.Environment)},{e.Level},{EscapeCsv(e.Message)},{EscapeCsv(e.Category)},{e.CorrelationId},{e.UserId},{EscapeCsv(e.ExceptionMessage)}");
        }
        return Results.Text(sb.ToString(), "text/csv", Encoding.UTF8);
    }

    return Results.Json(events);
})
.WithName("ExportEvents");

// === Security: Banned IPs ===

// Nettoie les bans expirés au démarrage
{
    var factory = app.Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
    using var db = await factory.CreateDbContextAsync();
    var expired = await db.BannedIps
        .Where(b => b.IsActive && b.ExpiresAt != null && b.ExpiresAt < DateTimeOffset.UtcNow)
        .ToListAsync();
    if (expired.Any())
    {
        foreach (var b in expired) b.IsActive = false;
        await db.SaveChangesAsync();
    }
}

app.MapGet("/api/ai/status", ([FromServices] IConfiguration config) =>
{
    var hasKey = !string.IsNullOrEmpty(config["AiAnalyst:GeminiApiKey"]);
    var interval = config["AiAnalyst:IntervalMinutes"] ?? "15";
    return Results.Ok(new { enabled = hasKey, intervalMinutes = interval });
}).WithName("AiStatus");

app.MapGet("/api/security/bans", async ([FromServices] IDbContextFactory<AppDbContext> dbFactory) =>
{
    using var db = await dbFactory.CreateDbContextAsync();
    var bans = await db.BannedIps.Where(b => b.IsActive).OrderByDescending(b => b.BannedAt).ToListAsync();
    return Results.Ok(bans);
}).WithName("ListBans");

app.MapPost("/api/security/ban", async ([FromBody] BanRequest request, [FromServices] IDbContextFactory<AppDbContext> dbFactory) =>
{
    if (string.IsNullOrWhiteSpace(request.IpAddress))
        return Results.BadRequest("IP address required.");

    using var db = await dbFactory.CreateDbContextAsync();
    var existing = await db.BannedIps.FirstOrDefaultAsync(b => b.IpAddress == request.IpAddress);
    if (existing != null && existing.IsActive) return Results.Conflict("IP already banned.");

    if (existing != null)
    {
        existing.BanCount++;
        existing.IsActive = true;
        existing.Reason = request.Reason;
        existing.BannedAt = DateTimeOffset.UtcNow;
        existing.ExpiresAt = existing.BanCount switch
        {
            1 => DateTimeOffset.UtcNow.AddDays(1),
            2 => DateTimeOffset.UtcNow.AddDays(7),
            _ => DateTimeOffset.UtcNow.AddDays(30)
        };
    }
    else
    {
        db.BannedIps.Add(new BannedIp
        {
            IpAddress = request.IpAddress,
            Reason = request.Reason,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
        });
    }
    await db.SaveChangesAsync();

    return Results.Ok(new { banned = request.IpAddress, request.Reason });
}).WithName("BanIp");

app.MapDelete("/api/security/ban/{ip}", async (string ip, [FromServices] IDbContextFactory<AppDbContext> dbFactory) =>
{
    using var db = await dbFactory.CreateDbContextAsync();
    var ban = await db.BannedIps.FirstOrDefaultAsync(b => b.IpAddress == ip && b.IsActive);
    if (ban == null) return Results.NotFound();

    ban.IsActive = false;
    await db.SaveChangesAsync();

    return Results.Ok(new { unbanned = ip });
}).WithName("UnbanIp");

app.Run();

record BanRequest(string IpAddress, string? Reason, int? DurationHours = null);
