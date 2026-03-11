using FeelAutoMetrics.Infrastructure.Data;
using FeelAutoMetrics.Infrastructure.Services;
using FeelAutoMetrics.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddOpenApi();

// DB Configuration
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString, b => b.MigrationsAssembly("FeelAutoMetrics.Ingestor")));

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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

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

app.Run();
