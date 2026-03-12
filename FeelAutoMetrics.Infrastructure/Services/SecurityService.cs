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

    private static readonly string[] ThreatPaths =
    [
        "/wp-admin", "/wp-login", "/wp-content", "/wp-includes", "/xmlrpc.php", "/wp-json",
        "/.env", "/.git", "/.svn", "/.htaccess", "/.htpasswd", "/web.config",
        "/appsettings", "/.aws", "/.docker", "/.ssh",
        "/phpmyadmin", "/pma", "/adminer", "/cpanel",
        "/shell", "/cmd", "/eval", "/exec", "/cgi-bin",
        "/etc/passwd", "/etc/shadow", "/win.ini", "/boot.ini",
        "/.vscode", "/.idea", "/.DS_Store",
        "/actuator", "/debug", "/trace", "/swagger",
        "/vendor/autoload", "/node_modules", "/backup", "/dump",
        "/telescope", "/horizon", "/elfinder", "/filemanager",
        "/solr", "/jenkins", "/struts", "/console",
        "/${jndi", "/spring",
        "/fileupload", "/file-upload", "/uploadfile",
        "/api/storage", "/api/blob", "/api/media",
        "/rest/settings", "/api/batch/upload", "/api/bulk-upload"
    ];

    private static readonly string[] SuspiciousExtensions =
    [
        ".php", ".asp", ".aspx", ".jsp", ".cgi", ".pl",
        ".sql", ".bak", ".old", ".orig", ".swp", ".tmp",
        ".zip", ".tar", ".gz", ".rar", ".7z", ".config"
    ];

    private static readonly ConcurrentDictionary<string, byte> _excludedIpsCache = new();
    private static DateTimeOffset _lastExcludedCacheRefresh = DateTimeOffset.MinValue;
    private static readonly object _excludedLock = new();

    public SecurityService(IDbContextFactory<AppDbContext> dbFactory, ILogger<SecurityService> logger, IConfiguration config)
    {
        _dbFactory = dbFactory;
        _logger = logger;
        _config = config;
    }

    private async Task EnsureExcludedCacheAsync()
    {
        if (DateTimeOffset.UtcNow - _lastExcludedCacheRefresh < TimeSpan.FromMinutes(5)) return;

        lock (_excludedLock)
        {
            if (DateTimeOffset.UtcNow - _lastExcludedCacheRefresh < TimeSpan.FromMinutes(5)) return;
        }

        try
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            var ips = await db.ExcludedIps.Select(e => e.IpAddress).ToListAsync();
            
            _excludedIpsCache.Clear();
            foreach (var ip in ips) _excludedIpsCache.TryAdd(ip, 0);
            
            _lastExcludedCacheRefresh = DateTimeOffset.UtcNow;
            _logger.LogInformation("Security cache: {Count} excluded IPs loaded.", ips.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing excluded IPs cache.");
        }
    }

    private static readonly string[] CloudflareRanges =
    [
        "172.68.", "172.69.", "172.70.", "172.71.",
        "172.64.", "172.65.", "172.66.", "172.67.",
        "104.16.", "104.17.", "104.18.", "104.19.", "104.20.",
        "104.21.", "104.22.", "104.23.", "104.24.", "104.25.",
        "162.158.", "141.101.", "108.162.", "190.93.",
        "188.114.", "197.234.", "198.41.", "103.21.",
        "103.22.", "103.31."
    ];

    public static bool IsCloudflareIp(string ip)
    {
        if (string.IsNullOrEmpty(ip)) return false;
        return CloudflareRanges.Any(r => ip.StartsWith(r));
    }

    public bool IsWhitelisted(string ip)
    {
        if (ip is "127.0.0.1" or "::1" || ip.StartsWith("172.17.") || ip.StartsWith("172.18."))
            return true;
        if (IsCloudflareIp(ip)) return true;

        if (_excludedIpsCache.ContainsKey(ip)) return true;

        var allowedIps = _config["Security:WhitelistedIps"];
        if (string.IsNullOrEmpty(allowedIps)) return false;
        return allowedIps.Split(',').Select(i => i.Trim()).Contains(ip);
    }

    public async Task AnalyzeLogAsync(GlobalAccessLog log)
    {
        await EnsureExcludedCacheAsync();
        if (IsWhitelisted(log.ClientHost)) return;

        string? threatType = null;
        int points = 0;

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

        if (threatType == null && IsIpAddress(log.RequestHost))
        {
            threatType = "DirectIpScan";
            points = 50;
        }

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

        if (threatType == null && (log.RequestPath.Contains("../") || log.RequestPath.Contains("..\\")))
        {
            threatType = "PathTraversal";
            points = 50;
        }

        if (threatType == null && (pathLower.Contains("union+select") || pathLower.Contains("' or ") ||
            pathLower.Contains("1=1") || pathLower.Contains("drop+table")))
        {
            threatType = "SQLInjection";
            points = 50;
        }

        if (threatType == null && (pathLower.Contains("<script") || pathLower.Contains("javascript:") ||
            pathLower.Contains("onerror=")))
        {
            threatType = "XSS";
            points = 50;
        }

        if (threatType == null && log.ResponseStatusCode == 404)
        {
            threatType = "NotFound";
            points = 2;
        }

        if (threatType == null && log.ResponseStatusCode >= 500)
        {
            var now500 = DateTimeOffset.UtcNow;
            var burst = _error500Bursts.AddOrUpdate(log.ClientHost,
                (1, now500),
                (_, old) =>
                {
                    if ((now500 - old.WindowStart).TotalSeconds > 60)
                        return (1, now500);
                    return (old.Count + 1, old.WindowStart);
                });

            if (burst.Count >= 5)
            {
                threatType = "Error500Burst";
                points = 100;
                _error500Bursts.TryRemove(log.ClientHost, out _);
            }
        }

        if (threatType == null && log.IsBot && log.BotCategory == "ThreatScanner")
        {
            threatType = "ThreatBot";
            points = 20;
        }

        {
            var isStaticAsset = pathLower.StartsWith("/_next/") ||
                                pathLower.StartsWith("/favicon") ||
                                pathLower.Contains("/_rsc=") ||
                                pathLower.EndsWith(".css") ||
                                pathLower.EndsWith(".js") ||
                                pathLower.EndsWith(".png") ||
                                pathLower.EndsWith(".jpg") ||
                                pathLower.EndsWith(".svg") ||
                                pathLower.EndsWith(".woff2") ||
                                pathLower.EndsWith(".ico");

            if (!isStaticAsset)
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

                if (reqBurst.Count >= 60 && threatType == null)
                {
                    threatType = "RequestBurst";
                    points = 100;
                    _requestBursts.TryRemove(log.ClientHost, out _);
                }
            }
        }

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

        if (points > 0)
        {
            await ReportThreatAsync(log.ClientHost, points, $"{threatType}: {log.RequestPath}");
        }
    }

    private async Task ReportThreatAsync(string ip, int points, string reason)
    {
        var now = DateTimeOffset.UtcNow;

        if (!_threatScores.ContainsKey(ip))
        {
            try
            {
                using var dbRestore = await _dbFactory.CreateDbContextAsync();
                var dbScore = await dbRestore.IpThreatScores.FindAsync(ip);
                if (dbScore != null)
                    _threatScores.TryAdd(ip, (dbScore.Score, dbScore.LastHit));
            }
            catch { }
        }

        int banCount = 0;
        try
        {
            using var dbBan = await _dbFactory.CreateDbContextAsync();
            var ban = await dbBan.BannedIps.AsNoTracking().FirstOrDefaultAsync(b => b.IpAddress == ip);
            banCount = ban?.BanCount ?? 0;
        }
        catch { }

        int multiplier = Math.Max(1, banCount * 2);

        if (banCount >= 5)
        {
            points = 200;
        }
        else
        {
            points *= multiplier;
        }

        var entry = _threatScores.AddOrUpdate(ip,
            (points, now),
            (key, old) => (old.Score + points, now));

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
            catch { }
        });

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
                if (existing.IsActive) return;

                var now = DateTimeOffset.UtcNow;
                existing.BanCount++;
                existing.IsActive = true;
                existing.Reason = reason;
                existing.BannedAt = now;
                existing.ExpiresAt = existing.BanCount switch
                {
                    1 => now.AddDays(1),
                    2 => now.AddDays(7),
                    _ => now.AddDays(30)
                };
            }
            else
            {
                var now = DateTimeOffset.UtcNow;
                db.BannedIps.Add(new BannedIp
                {
                    IpAddress = ip,
                    Reason = reason,
                    BannedAt = now,
                    ExpiresAt = now.AddDays(1),
                    BanCount = 1,
                    IsActive = true
                });
            }

            await db.SaveChangesAsync();
            _logger.LogWarning("AUTO-BAN: {IP} — {Reason}", ip, reason);

            var scoreEntry = await db.IpThreatScores.FindAsync(ip);
            if (scoreEntry != null)
            {
                db.IpThreatScores.Remove(scoreEntry);
                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur auto-ban pour {IP}", ip);
        }
    }

    private static bool IsIpAddress(string host)
    {
        if (string.IsNullOrEmpty(host)) return false;
        return System.Net.IPAddress.TryParse(host, out _);
    }
}
