using FeelAutoMetrics.Shared.Models;

namespace FeelAutoMetrics.Infrastructure.Services;

public interface ISecurityService
{
    Task AnalyzeLogAsync(GlobalAccessLog log);
}

public class SecurityService : ISecurityService
{
    private static readonly string[] ThreatPatterns = 
    [
        "/wp-admin", "/.env", "/.git", "/config", "/phpmyadmin", "/xmlrpc.php", 
        "/.vscode", "/.ssh", "/api/v1/auth/login", "/etc/passwd", "/win.ini"
    ];

    public Task AnalyzeLogAsync(GlobalAccessLog log)
    {
        // Simple pattern matching for threats
        foreach (var pattern in ThreatPatterns)
        {
            if (log.RequestPath.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                log.IsBot = true;
                log.BotCategory = "ThreatScanner";
                // TODO: Could trigger a global ban alert or mark IP in a 'ThreatIPs' table
                break;
            }
        }

        // Logic for aggressive scrapers
        if (log.ResponseStatusCode == 404 && log.IsBot)
        {
            // Possible scan
        }

        return Task.CompletedTask;
    }
}
