using FeelAutoMetrics.Infrastructure.Data;
using FeelAutoMetrics.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace FeelAutoMetrics.Infrastructure.Services;

public interface ISecurityService
{
    Task AnalyzeLogAsync(GlobalAccessLog log);
    bool IsWhitelisted(string ip);
}

public class SecurityService : ISecurityService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<SecurityService> _logger;
    private readonly IConfiguration _config;
    private static readonly ConcurrentDictionary<string, (int Score, DateTimeOffset LastHit)> _threatScores = new();
    private static readonly ConcurrentDictionary<string, (int Count, DateTimeOffset WindowStart)> _error500Bursts = new();
    private static readonly ConcurrentDictionary<string, (int Count, DateTimeOffset WindowStart)> _requestBursts = new();
    private const string BanFilePath = "/app/Security/banned-ips.txt";

    // Paths classiques de scan/intrusion
    private static readonly string[] ThreatPaths =
    [
        // WordPress / CMS
        "/wp-admin", "/wp-login", "/wp-content", "/wp-includes", "/xmlrpc.php", "/wp-json",
        // Config & secrets
        "/.env", "/.git", "/.svn", "/.htaccess", "/.htpasswd", "/web.config",
        "/appsettings", "/.aws", "/.docker", "/.ssh",
        // Admin panels
        "/phpmyadmin", "/pma", "/adminer", "/cpanel",
        // Shell & exploit
        "/shell", "/cmd", "/eval", "/exec", "/cgi-bin",
        // System files
        "/etc/passwd", "/etc/shadow", "/win.ini", "/boot.ini",
        // Dev tools
        "/.vscode", "/.idea", "/.DS_Store",
        // API probing
        "/actuator", "/debug", "/trace", "/swagger",
        // Common vuln paths (pas /vendor seul, trop de FP avec assets bundlés)
        "/vendor/autoload", "/node_modules", "/backup", "/dump",
        "/telescope", "/horizon", "/elfinder", "/filemanager",
        "/solr", "/jenkins", "/struts", "/console",
        // Log4Shell, Spring4Shell
        "/${jndi", "/spring",
        // Upload probing (paths exacts, pas /uploads/fichier.ext qui est légitime sur un CMS)
        "/fileupload", "/file-upload", "/uploadfile",
        "/api/storage", "/api/blob", "/api/media",
        // REST settings probing
        "/rest/settings", "/api/batch/upload", "/api/bulk-upload"
    ];

    // Extensions suspectes
    private static readonly string[] SuspiciousExtensions =
    [
        ".php", ".asp", ".aspx", ".jsp", ".cgi", ".pl",
        ".sql", ".bak", ".old", ".orig", ".swp", ".tmp",
        ".zip", ".tar", ".gz", ".rar", ".7z", ".config"
    ];

    public SecurityService(IDbContextFactory<AppDbContext> dbFactory, ILogger<SecurityService> logger, IConfiguration config)
    {
        _dbFactory = dbFactory;
        _logger = logger;
        _config = config;
    }

    public bool IsWhitelisted(string ip)
    {
        if (ip is "127.0.0.1" or "::1" || ip.StartsWith("172.17.") || ip.StartsWith("172.18."))
            return true;
        var allowedIps = _config["Security:WhitelistedIps"];
        if (string.IsNullOrEmpty(allowedIps)) return false;
        return allowedIps.Split(',').Select(i => i.Trim()).Contains(ip);
    }

    public async Task AnalyzeLogAsync(GlobalAccessLog log)
    {
        if (IsWhitelisted(log.ClientHost)) return;

        string? threatType = null;
        int points = 0;

        // 1. Threat path matching (scan de vulnérabilités)
        var pathLower = log.RequestPath.ToLowerInvariant();
        foreach (var pattern in ThreatPaths)
        {
            if (pathLower.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                threatType = "PathScan";
                points = 20;
                break;
            }
        }

        // 2. Scan sur IP directe (RequestHost = IP au lieu d'un domaine)
        // 50pts = ban après ~4 requêtes (compromis entre sécurité et faux positifs)
        if (threatType == null && IsIpAddress(log.RequestHost))
        {
            threatType = "DirectIpScan";
            points = 50;
        }

        // 3. Extensions suspectes (.php, .sql, .bak sur un site .NET)
        if (threatType == null)
        {
            foreach (var ext in SuspiciousExtensions)
            {
                if (pathLower.EndsWith(ext))
                {
                    threatType = "SuspiciousExtension";
                    points = 20;
                    break;
                }
            }
        }

        // 4. Path traversal
        if (threatType == null && (log.RequestPath.Contains("../") || log.RequestPath.Contains("..\\")))
        {
            threatType = "PathTraversal";
            points = 50;
        }

        // 5. SQL injection patterns
        if (threatType == null && (pathLower.Contains("union+select") || pathLower.Contains("' or ") ||
            pathLower.Contains("1=1") || pathLower.Contains("drop+table")))
        {
            threatType = "SQLInjection";
            points = 50;
        }

        // 6. XSS patterns
        if (threatType == null && (pathLower.Contains("<script") || pathLower.Contains("javascript:") ||
            pathLower.Contains("onerror=")))
        {
            threatType = "XSS";
            points = 50;
        }

        // 7. 404 scoring (rafales de 404 = scan)
        if (threatType == null && log.ResponseStatusCode == 404)
        {
            threatType = "NotFound";
            points = 2;
        }

        // 8. Rafale d'erreurs 500 (ex: LeakIX — provoque des crashs en série)
        if (threatType == null && log.ResponseStatusCode >= 500)
        {
            var now500 = DateTimeOffset.UtcNow;
            var burst = _error500Bursts.AddOrUpdate(log.ClientHost,
                (1, now500),
                (_, old) =>
                {
                    // Reset la fenêtre si > 60 secondes
                    if ((now500 - old.WindowStart).TotalSeconds > 60)
                        return (1, now500);
                    return (old.Count + 1, old.WindowStart);
                });

            if (burst.Count >= 5) // 5+ erreurs 500 en 60s = scan agressif
            {
                threatType = "Error500Burst";
                points = 100; // Ban rapide (200 en 2 rafales)
                _error500Bursts.TryRemove(log.ClientHost, out _);
            }
        }

        // 9. ThreatScanner bots (déjà détectés par UserAgentService)
        if (threatType == null && log.IsBot && log.BotCategory == "ThreatScanner")
        {
            threatType = "ThreatBot";
            points = 20;
        }

        // 10. Rafale de requêtes (scraping/crawling agressif — 30+ req en 10s)
        {
            var nowReq = DateTimeOffset.UtcNow;
            var reqBurst = _requestBursts.AddOrUpdate(log.ClientHost,
                (1, nowReq),
                (_, old) =>
                {
                    if ((nowReq - old.WindowStart).TotalSeconds > 10)
                        return (1, nowReq);
                    return (old.Count + 1, old.WindowStart);
                });

            if (reqBurst.Count >= 30 && threatType == null)
            {
                threatType = "RequestBurst";
                points = 50; // Ban après ~4 rafales (200pts)
                _requestBursts.TryRemove(log.ClientHost, out _);
            }
        }

        // Appliquer le flag IsSuspicious pour les détections directes (hors 404 simples)
        if (threatType != null && threatType != "NotFound")
        {
            log.IsSuspicious = true;
            log.ThreatType = threatType;
            if (!log.IsBot)
            {
                log.IsBot = true;
                log.BotCategory = "ThreatScanner";
            }
        }

        // Reporter la menace au système de scoring
        if (points > 0)
        {
            await ReportThreatAsync(log.ClientHost, points, $"{threatType}: {log.RequestPath}");
        }
    }

    private async Task ReportThreatAsync(string ip, int points, string reason)
    {
        var now = DateTimeOffset.UtcNow;

        // Restauration depuis BDD si absent de la mémoire (redémarrage)
        if (!_threatScores.ContainsKey(ip))
        {
            try
            {
                using var dbRestore = await _dbFactory.CreateDbContextAsync();
                var dbScore = await dbRestore.IpThreatScores.FindAsync(ip);
                if (dbScore != null)
                    _threatScores.TryAdd(ip, (dbScore.Score, dbScore.LastHit));
            }
            catch { /* Best effort */ }
        }

        // Multiplicateur récidive
        int banCount = 0;
        try
        {
            using var dbBan = await _dbFactory.CreateDbContextAsync();
            var ban = await dbBan.BannedIps.AsNoTracking().FirstOrDefaultAsync(b => b.IpAddress == ip);
            banCount = ban?.BanCount ?? 0;
        }
        catch { /* Best effort */ }

        int multiplier = Math.Max(1, banCount * 2);

        // Récidivistes 5+ : ban instantané
        if (banCount >= 5)
        {
            points = 200;
        }
        else
        {
            points *= multiplier;
        }

        // Accumulation permanente
        var entry = _threatScores.AddOrUpdate(ip,
            (points, now),
            (key, old) => (old.Score + points, now));

        // Persistance BDD (fire-and-forget)
        _ = Task.Run(async () =>
        {
            try
            {
                using var db = await _dbFactory.CreateDbContextAsync();
                var existing = await db.IpThreatScores.FindAsync(ip);
                if (existing != null)
                {
                    existing.Score = entry.Score;
                    existing.LastHit = entry.LastHit;
                }
                else
                {
                    db.IpThreatScores.Add(new IpThreatScore
                    {
                        IpAddress = ip,
                        Score = entry.Score,
                        LastHit = entry.LastHit,
                        FirstSeen = now
                    });
                }
                await db.SaveChangesAsync();
            }
            catch { /* Best effort */ }
        });

        // Auto-ban à 200 points
        if (entry.Score >= 200)
        {
            await AutoBanAsync(ip, reason + $" (Score: {entry.Score})");
            _threatScores.TryRemove(ip, out _);
        }
    }

    private async Task AutoBanAsync(string ip, string reason)
    {
        try
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            var existing = await db.BannedIps.FirstOrDefaultAsync(b => b.IpAddress == ip);

            if (existing != null)
            {
                if (existing.IsActive) return; // Déjà banni

                existing.BanCount++;
                existing.IsActive = true;
                existing.Reason = reason;
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
                    IpAddress = ip,
                    Reason = reason,
                    BannedAt = DateTimeOffset.UtcNow,
                    ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                    BanCount = 1,
                    IsActive = true
                });
            }

            await db.SaveChangesAsync();

            _logger.LogWarning("AUTO-BAN: {IP} — {Reason}", ip, reason);

            // Reset le score après ban
            var scoreEntry = await db.IpThreatScores.FindAsync(ip);
            if (scoreEntry != null)
            {
                db.IpThreatScores.Remove(scoreEntry);
                await db.SaveChangesAsync();
            }

            // Sync fichier pour iptables
            await SyncBanFileAsync(db);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur auto-ban pour {IP}", ip);
        }
    }

    private async Task SyncBanFileAsync(AppDbContext db)
    {
        try
        {
            var ips = await db.BannedIps.Where(b => b.IsActive).Select(b => b.IpAddress).ToListAsync();
            var dir = Path.GetDirectoryName(BanFilePath);
            if (dir != null) Directory.CreateDirectory(dir);
            await File.WriteAllLinesAsync(BanFilePath, ips);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur sync fichier ban");
        }
    }

    private static bool IsIpAddress(string host)
    {
        if (string.IsNullOrEmpty(host)) return false;
        return System.Net.IPAddress.TryParse(host, out _);
    }
}
