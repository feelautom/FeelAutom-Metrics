using FeelAutoMetrics.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FeelAutoMetrics.Infrastructure.Services;

public class LogRetentionService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LogRetentionService> _logger;
    private readonly int _retentionDays;

    public LogRetentionService(
        IServiceScopeFactory scopeFactory,
        ILogger<LogRetentionService> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        var configValue = configuration["Retention:Days"];
        _retentionDays = int.TryParse(configValue, out var days) ? days : 60;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LogRetentionService started. Retention: {Days} days.", _retentionDays);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PurgeOldLogs(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during log retention purge.");
            }

            // Run once per day
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }

    private async Task PurgeOldLogs(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        using var context = await factory.CreateDbContextAsync(cancellationToken);

        var cutoff = DateTimeOffset.UtcNow.AddDays(-_retentionDays);

        var deletedLogs = await context.GlobalAccessLogs
            .Where(l => l.Timestamp < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        var deletedEvents = await context.AppEvents
            .Where(e => e.Timestamp < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        // Rotation des analyses IA : garder 24h max
        var aiCutoff = DateTimeOffset.UtcNow.AddDays(-1);
        var deletedAi = await context.AiAnalyses
            .Where(a => a.AnalyzedAt < aiCutoff)
            .ExecuteDeleteAsync(cancellationToken);

        if (deletedLogs > 0 || deletedEvents > 0 || deletedAi > 0)
        {
            _logger.LogInformation(
                "Retention purge complete: {Logs} logs, {Events} events, {Ai} AI analyses deleted.",
                deletedLogs, deletedEvents, deletedAi);
        }
    }
}
