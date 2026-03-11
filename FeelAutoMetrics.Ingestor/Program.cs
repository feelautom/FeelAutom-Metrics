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

builder.Services.AddDbContext<AppDbContext>(options =>
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

var app = builder.Build();

// Auto-migrate on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Pas de UseHttpsRedirection() : Traefik gère le TLS en amont

// Root endpoint for health check
app.MapGet("/", () => Results.Ok(new
{
    Name = "FeelAuto-Metrics Ingestor",
    Status = "Online",
    Time = DateTimeOffset.UtcNow
}))
.WithName("HealthCheck");

// Ingestion Endpoint (Traefik Logs)
app.MapPost("/api/logs/ingest", async ([FromBody] object rawLog, [FromServices] ILogProcessor processor, [FromServices] AppDbContext db) =>
{
    try
    {
        var rawJson = rawLog.ToString();
        if (string.IsNullOrEmpty(rawJson)) return Results.BadRequest("Empty log body.");

        var log = await processor.ProcessAsync(rawJson);
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
app.MapPost("/api/events", async ([FromBody] AppEvent appEvent, [FromServices] AppDbContext db) =>
{
    try
    {
        if (appEvent.Timestamp == default) appEvent.Timestamp = DateTimeOffset.UtcNow;

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
    [FromServices] AppDbContext db) =>
{
    var fmt = (format ?? "json").ToLowerInvariant();

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
    [FromServices] AppDbContext db) =>
{
    var fmt = (format ?? "json").ToLowerInvariant();

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

const string BanFilePath = "/app/Security/banned-ips.txt";

// Nettoie les bans expirés et écrit la liste active dans un fichier pour sync iptables
async Task SyncBanFile(AppDbContext db)
{
    // Supprimer les bans expirés
    var expired = await db.BannedIps
        .Where(b => b.ExpiresAt != null && b.ExpiresAt < DateTimeOffset.UtcNow)
        .ToListAsync();
    if (expired.Any())
    {
        db.BannedIps.RemoveRange(expired);
        await db.SaveChangesAsync();
    }

    var ips = await db.BannedIps.Select(b => b.IpAddress).ToListAsync();
    var dir = Path.GetDirectoryName(BanFilePath);
    if (dir != null) Directory.CreateDirectory(dir);
    await File.WriteAllLinesAsync(BanFilePath, ips);
}

// Init: sync le fichier au démarrage
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await SyncBanFile(db);
}

app.MapGet("/api/security/bans", async ([FromServices] AppDbContext db) =>
{
    var bans = await db.BannedIps.OrderByDescending(b => b.BannedAt).ToListAsync();
    return Results.Ok(bans);
}).WithName("ListBans");

app.MapPost("/api/security/ban", async ([FromBody] BanRequest request, [FromServices] AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(request.IpAddress))
        return Results.BadRequest("IP address required.");

    var exists = await db.BannedIps.AnyAsync(b => b.IpAddress == request.IpAddress);
    if (exists) return Results.Conflict("IP already banned.");

    var ban = new BannedIp
    {
        IpAddress = request.IpAddress,
        Reason = request.Reason,
        ExpiresAt = request.DurationHours.HasValue
            ? DateTimeOffset.UtcNow.AddHours(request.DurationHours.Value)
            : null // null = permanent
    };
    db.BannedIps.Add(ban);
    await db.SaveChangesAsync();
    await SyncBanFile(db);

    return Results.Ok(new { banned = ban.IpAddress, ban.Reason, ban.BannedAt });
}).WithName("BanIp");

app.MapDelete("/api/security/ban/{ip}", async (string ip, [FromServices] AppDbContext db) =>
{
    var ban = await db.BannedIps.FirstOrDefaultAsync(b => b.IpAddress == ip);
    if (ban == null) return Results.NotFound();

    db.BannedIps.Remove(ban);
    await db.SaveChangesAsync();
    await SyncBanFile(db);

    return Results.Ok(new { unbanned = ip });
}).WithName("UnbanIp");

app.Run();

record BanRequest(string IpAddress, string? Reason, int? DurationHours = null);
