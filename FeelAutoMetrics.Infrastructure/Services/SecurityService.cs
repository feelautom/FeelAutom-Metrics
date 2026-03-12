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

    // Cache mémoire pour le scoring en temps réel (évite les écritures DB trop fréquentes)
    private static readonly ConcurrentDictionary<string, (int Score, DateTimeOffset LastHit)> _threatScores = new();
    
    // Fenêtres de détection pour les rafales (bursts)
    private static readonly ConcurrentDictionary<string, (int Count, DateTimeOffset WindowStart)> _error500Bursts = new();
    private static readonly ConcurrentDictionary<string, (int Count, DateTimeOffset WindowStart)> _requestBursts = new();

    // === SIGNATURES DE MENACES ===

    // Chemins classiques utilisés par les bots de scan, scripts malveillants et hackers
    private static readonly string[] ThreatPaths =
    [
        // CMS & Frameworks (WordPress, etc.)
        "/wp-admin", "/wp-login", "/wp-content", "/wp-includes", "/xmlrpc.php", "/wp-json",
        // Fichiers de configuration et secrets
        "/.env", "/.git", "/.svn", "/.htaccess", "/.htpasswd", "/web.config",
        "/appsettings", "/.aws", "/.docker", "/.ssh",
        // Interfaces d'administration
        "/phpmyadmin", "/pma", "/adminer", "/cpanel",
        // Webshells et commandes distantes
        "/shell", "/cmd", "/eval", "/exec", "/cgi-bin",
        // Fichiers système sensibles
        "/etc/passwd", "/etc/shadow", "/win.ini", "/boot.ini",
        // Outils de développement
        "/.vscode", "/.idea", "/.DS_Store",
        // Endpoints de debug et metrics
        "/actuator", "/debug", "/trace", "/swagger",
        // Dépendances et backdoors communes
        "/vendor/autoload", "/node_modules", "/backup", "/dump",
        "/telescope", "/horizon", "/elfinder", "/filemanager",
        "/solr", "/jenkins", "/struts", "/console",
        // Exploits Log4Shell, Spring4Shell
        "/${jndi", "/spring",
        // Probing d'upload
        "/fileupload", "/file-upload", "/uploadfile",
        "/api/storage", "/api/blob", "/api/media",
        // Paramètres REST
        "/rest/settings", "/api/batch/upload", "/api/bulk-upload"
    ];

    // Extensions de fichiers suspectes pour un serveur web moderne
    private static readonly string[] SuspiciousExtensions =
    [
        ".php", ".asp", ".aspx", ".jsp", ".cgi", ".pl",
        ".sql", ".bak", ".old", ".orig", ".swp", ".tmp",
        ".zip", ".tar", ".gz", ".rar", ".7z", ".config"
    ];

    // Cache local pour les IPs exclues (whitelist dynamique du dashboard)
    private static readonly ConcurrentDictionary<string, byte> _excludedIpsCache = new();
    private static DateTimeOffset _lastExcludedCacheRefresh = DateTimeOffset.MinValue;
    private static readonly object _excludedLock = new();

    public SecurityService(IDbContextFactory<AppDbContext> dbFactory, ILogger<SecurityService> logger, IConfiguration config)
    {
        _dbFactory = dbFactory;
        _logger = logger;
        _config = config;
    }

    /// <summary>
    /// S'assure que le cache des IPs exclues est à jour (refresh toutes les 5 min).
    /// </summary>
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

    // Plages IP Cloudflare — Ne jamais bannir car ce sont des proxys légitimes
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

    /// <summary>
    /// Vérifie si une IP est en liste blanche (Statique ou Dynamique).
    /// </summary>
    public bool IsWhitelisted(string ip)
    {
        // 1. IPs locales et réseaux internes
        if (ip is "127.0.0.1" or "::1" || ip.StartsWith("172.17.") || ip.StartsWith("172.18."))
            return true;

        // 2. Proxies Cloudflare
        if (IsCloudflareIp(ip)) return true;

        // 3. Cache de la table ExcludedIps (Dashboard)
        if (_excludedIpsCache.ContainsKey(ip)) return true;

        // 4. Variable d'environnement maître
        var allowedIps = _config["Security:WhitelistedIps"];
        if (string.IsNullOrEmpty(allowedIps)) return false;
        return allowedIps.Split(',').Select(i => i.Trim()).Contains(ip);
    }

    /// <summary>
    /// Analyse un log d'accès pour détecter des comportements malveillants.
    /// </summary>
    public async Task AnalyzeLogAsync(GlobalAccessLog log)
    {
        await EnsureExcludedCacheAsync();
        
        // On ne traite pas les whitelists
        if (IsWhitelisted(log.ClientHost)) return;

        string? threatType = null;
        int points = 0;

        var pathLower = log.RequestPath.ToLowerInvariant();

        // 1. Scan de vulnérabilités (Matching de chemins connus)
        foreach (var pattern in ThreatPaths)
        {
            if (pathLower.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                threatType = "PathScan";
                points = 20;
                break;
            }
        }

        // 2. Scan par IP directe (RequestHost est l'IP au lieu du domaine)
        if (threatType == null && IsIpAddress(log.RequestHost))
        {
            threatType = "DirectIpScan";
            points = 50; // Plus sévère car rare pour un humain
        }

        // 3. Extensions de fichiers suspectes (.php, .config sur .NET...)
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

        // 4. Tentative de traversée de répertoire (../)
        if (threatType == null && (log.RequestPath.Contains("../") || log.RequestPath.Contains("..\\")))
        {
            threatType = "PathTraversal";
            points = 50;
        }

        // 5. Patterns d'injection SQL
        if (threatType == null && (pathLower.Contains("union+select") || pathLower.Contains("' or ") ||
            pathLower.Contains("1=1") || pathLower.Contains("drop+table")))
        {
            threatType = "SQLInjection";
            points = 50;
        }

        // 6. Patterns XSS
        if (threatType == null && (pathLower.Contains("<script") || pathLower.Contains("javascript:") ||
            pathLower.Contains("onerror=")))
        {
            threatType = "XSS";
            points = 50;
        }

        // 7. Scoring des 404 (Scans de fichiers inexistants)
        if (threatType == null && log.ResponseStatusCode == 404)
        {
            threatType = "NotFound";
            points = 2; // Accumulation lente pour les 404 simples
        }

        // 8. Rafale d'erreurs 500 (Attaques par déni de service ou plantage forcé)
        if (threatType == null && log.ResponseStatusCode >= 500)
        {
            var now500 = DateTimeOffset.UtcNow;
            var burst = _error500Bursts.AddOrUpdate(log.ClientHost,
                (1, now500),
                (_, old) =>
                {
                    // Reset la fenêtre après 60s
                    if ((now500 - old.WindowStart).TotalSeconds > 60)
                        return (1, now500);
                    return (old.Count + 1, old.WindowStart);
                });

            if (burst.Count >= 5) // 5+ erreurs serveur en 1 min = suspect
            {
                threatType = "Error500Burst";
                points = 100;
                _error500Bursts.TryRemove(log.ClientHost, out _);
            }
        }

        // 9. Bots de scan identifiés (via User-Agent)
        if (threatType == null && log.IsBot && log.BotCategory == "ThreatScanner")
        {
            threatType = "ThreatBot";
            points = 20;
        }

        // 10. Détection des rafales de requêtes (Scraping agressif / Brute force)
        {
            // On ignore les assets statiques et le prefetch Next.js
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
                        // Fenêtre de 10 secondes
                        if ((nowReq - old.WindowStart).TotalSeconds > 10)
                            return (1, nowReq);
                        return (old.Count + 1, old.WindowStart);
                    });

                if (reqBurst.Count >= 60 && threatType == null) // 6 pages/sec = bot
                {
                    threatType = "RequestBurst";
                    points = 100;
                    _requestBursts.TryRemove(log.ClientHost, out _);
                }
            }
        }

        // Enrichir le log avec les informations de menace
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

        // Envoi au système de scoring permanent
        if (points > 0)
        {
            await ReportThreatAsync(log.ClientHost, points, $"{threatType}: {log.RequestPath}");
        }
    }

    /// <summary>
    /// Gère l'accumulation des points et déclenche l'auto-ban à 200 pts.
    /// </summary>
    private async Task ReportThreatAsync(string ip, int points, string reason)
    {
        var now = DateTimeOffset.UtcNow;

        // Récupération du score actuel en mémoire ou base de données
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

        // GESTION DE LA RÉCIDIVE
        int banCount = 0;
        try
        {
            using var dbBan = await _dbFactory.CreateDbContextAsync();
            var ban = await dbBan.BannedIps.AsNoTracking().FirstOrDefaultAsync(b => b.IpAddress == ip);
            banCount = ban?.BanCount ?? 0;
        }
        catch { }

        // Multiplicateur : le score augmente de 2x par ban précédent
        int multiplier = Math.Max(1, banCount * 2);

        if (banCount >= 5)
        {
            // Récidivistes notoires : ban immédiat à la moindre alerte
            points = 200;
        }
        else
        {
            points *= multiplier;
        }

        // Mise à jour du score
        var entry = _threatScores.AddOrUpdate(ip,
            (points, now),
            (key, old) => (old.Score + points, now));

        // Persistance asynchrone (pour ne pas bloquer l'ingestion)
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

        // Déclenchement du bannissement au seuil critique
        if (entry.Score >= 200)
        {
            await AutoBanAsync(ip, reason + $" (Score: {entry.Score})");
            _threatScores.TryRemove(ip, out _);
        }
    }

    /// <summary>
    /// Applique le bannissement effectif dans la base de données.
    /// </summary>
    private async Task AutoBanAsync(string ip, string reason)
    {
        try
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            var existing = await db.BannedIps.FirstOrDefaultAsync(b => b.IpAddress == ip);
            var now = DateTimeOffset.UtcNow;

            if (existing != null)
            {
                if (existing.IsActive) return; // Déjà banni

                existing.BanCount++;
                existing.IsActive = true;
                existing.Reason = reason;
                existing.BannedAt = now;
                
                // Durée progressive selon le nombre de bans
                existing.ExpiresAt = existing.BanCount switch
                {
                    1 => now.AddDays(1),  // 1er ban : 24h
                    2 => now.AddDays(7),  // 2ème ban : 1 semaine
                    _ => now.AddDays(30)  // Récidive : 1 mois
                };
            }
            else
            {
                // Premier bannissement pour cette IP
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

            // Nettoyage final du score
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
