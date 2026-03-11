using FeelAutoMetrics.Infrastructure.Data;
using FeelAutoMetrics.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace FeelAutoMetrics.Infrastructure.Services;

public class AiAnalystService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<AiAnalystService> _logger;
    private readonly IConfiguration _config;
    private readonly HttpClient _httpClient;
    private readonly TimeSpan _interval;

    private const string GeminiBaseUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent";

    public AiAnalystService(IServiceProvider services, ILogger<AiAnalystService> logger, IConfiguration config)
    {
        _services = services;
        _logger = logger;
        _config = config;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        var intervalStr = config["AiAnalyst:IntervalMinutes"];
        var intervalMinutes = int.TryParse(intervalStr, out var iv) ? iv : 15;
        _interval = TimeSpan.FromMinutes(intervalMinutes);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var apiKey = _config["AiAnalyst:GeminiApiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("AI Analyst désactivé : clé API Gemini non configurée (AiAnalyst:GeminiApiKey)");
            return;
        }

        _logger.LogInformation("AI Analyst démarré — analyse toutes les {Interval} minutes", _interval.TotalMinutes);

        // Attendre 2 min après le démarrage pour laisser le système se stabiliser
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunAnalysisAsync(apiKey, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'analyse IA");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task RunAnalysisAsync(string apiKey, CancellationToken ct)
    {
        var dbFactory = _services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        using var db = await dbFactory.CreateDbContextAsync(ct);

        // Trouver le dernier point d'analyse
        var lastAnalysis = await db.AiAnalyses
            .OrderByDescending(a => a.LogsToTimestamp)
            .FirstOrDefaultAsync(ct);

        var since = lastAnalysis?.LogsToTimestamp ?? DateTimeOffset.UtcNow.AddMinutes(-30);
        var now = DateTimeOffset.UtcNow;

        // Récupérer les logs depuis la dernière analyse (max 2000 pour ne pas exploser le prompt)
        var logs = await db.GlobalAccessLogs
            .Where(l => l.Timestamp > since && l.Timestamp <= now)
            .OrderByDescending(l => l.Timestamp)
            .Take(2000)
            .ToListAsync(ct);

        if (logs.Count < 10)
        {
            _logger.LogDebug("AI Analyst : seulement {Count} logs, analyse reportée", logs.Count);
            return;
        }

        // Récupérer le contexte : bans actifs et scores en cours
        var activeBans = await db.BannedIps.Where(b => b.IsActive).Select(b => b.IpAddress).ToListAsync(ct);
        var excludedIps = await db.ExcludedIps.Select(e => e.IpAddress).ToListAsync(ct);
        var whitelistedIps = _config["Security:WhitelistedIps"]?.Split(',').Select(i => i.Trim()).ToList() ?? [];

        // Filtrer les IPs exclues et whitelistées des logs
        var allExcluded = new HashSet<string>(excludedIps.Concat(whitelistedIps));
        var filteredLogs = logs.Where(l => !allExcluded.Contains(l.ClientHost)).ToList();

        if (filteredLogs.Count < 5)
        {
            _logger.LogDebug("AI Analyst : pas assez de logs après filtrage, analyse reportée");
            return;
        }

        // Construire le résumé compact pour Gemini
        var prompt = BuildPrompt(filteredLogs, activeBans);

        // Appeler Gemini
        var response = await CallGeminiAsync(apiKey, prompt, ct);
        if (response == null)
        {
            // Enregistrer l'échec pour traçabilité
            db.AiAnalyses.Add(new AiAnalysis
            {
                LogsAnalyzed = filteredLogs.Count,
                ActionsTaken = 0,
                Summary = "Erreur : impossible de contacter Gemini API (voir logs pour détails).",
                LogsFromTimestamp = since,
                LogsToTimestamp = now
            });
            await db.SaveChangesAsync(ct);
            return;
        }

        // Parser et exécuter les actions
        var actions = ParseActions(response);
        int actionCount = 0;

        foreach (var action in actions)
        {
            if (allExcluded.Contains(action.Ip) || whitelistedIps.Contains(action.Ip))
            {
                _logger.LogWarning("AI Analyst a tenté d'agir sur une IP protégée {IP}, ignoré", action.Ip);
                continue;
            }

            if (action.Action == "ban" && !activeBans.Contains(action.Ip))
            {
                await BanFromAiAsync(db, action.Ip, action.Reason);
                actionCount++;
                _logger.LogWarning("AI-BAN: {IP} — {Reason}", action.Ip, action.Reason);
            }
        }

        // Sauvegarder l'analyse
        var summary = actions.Count > 0
            ? $"{actions.Count} décisions ({actionCount} bans appliqués). {response[..Math.Min(500, response.Length)]}"
            : "Aucune action requise.";

        db.AiAnalyses.Add(new AiAnalysis
        {
            LogsAnalyzed = filteredLogs.Count,
            ActionsTaken = actionCount,
            Summary = summary.Length > 4000 ? summary[..4000] : summary,
            LogsFromTimestamp = since,
            LogsToTimestamp = now
        });
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("AI Analyst : {LogCount} logs analysés, {Actions} actions", filteredLogs.Count, actionCount);
    }

    private string BuildPrompt(List<GlobalAccessLog> logs, List<string> activeBans)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Tu es un analyste SOC (Security Operations Center) pour une infrastructure web.");
        sb.AppendLine("Tu surveilles le trafic Traefik (reverse proxy) de plusieurs sites.");
        sb.AppendLine();
        sb.AppendLine("CONTEXTE :");
        sb.AppendLine($"- IPs actuellement bannies : {string.Join(", ", activeBans.Take(30))}");
        sb.AppendLine($"- Période analysée : {logs.Min(l => l.Timestamp):HH:mm:ss} → {logs.Max(l => l.Timestamp):HH:mm:ss}");
        sb.AppendLine($"- Nombre de logs : {logs.Count}");
        sb.AppendLine();
        sb.AppendLine("REGLES DE DECISION :");
        sb.AppendLine("- BANNIR : scanners de vulnérabilités, scrapers agressifs (rafales de requêtes inhumaines), bots déguisés en navigateurs qui crawlent à haute fréquence");
        sb.AppendLine("- BANNIR : IPs qui probing des paths sensibles (.env, .git, wp-admin, phpmyadmin, etc.)");
        sb.AppendLine("- BANNIR : IPs qui tapent directement sur l'IP du serveur (91.134.136.142) au lieu d'un domaine");
        sb.AppendLine("- NE PAS BANNIR : les bots légitimes (Googlebot, Bingbot, AhrefsBot, ClaudeBot, GPTBot, etc.)");
        sb.AppendLine("- NE PAS BANNIR : le trafic humain normal même s'il génère beaucoup de requêtes (Next.js prefetch génère des rafales de _rsc normales)");
        sb.AppendLine("- NE PAS BANNIR : les IPs déjà bannies");
        sb.AppendLine("- EN CAS DE DOUTE : ne pas bannir. Mieux vaut laisser passer que bloquer un utilisateur légitime.");
        sb.AppendLine();
        sb.AppendLine("LOGS (format: timestamp | domaine | IP | methode path | status | userAgent | isBot | botCategory | isSuspicious | threatType) :");
        sb.AppendLine();

        // Regrouper par IP pour donner plus de contexte
        var byIp = logs.GroupBy(l => l.ClientHost).OrderByDescending(g => g.Count());

        foreach (var group in byIp.Take(50)) // Top 50 IPs les plus actives
        {
            var isBanned = activeBans.Contains(group.Key);
            sb.AppendLine($"--- IP: {group.Key} ({group.Count()} requêtes){(isBanned ? " [DEJA BANNI]" : "")} ---");

            foreach (var log in group.Take(30)) // Max 30 lignes par IP
            {
                sb.AppendLine($"  {log.Timestamp:HH:mm:ss} | {log.RequestHost} | {log.RequestMethod} {log.RequestPath} | {log.ResponseStatusCode} | {log.BrowserName ?? log.UserAgentBrut[..Math.Min(40, log.UserAgentBrut.Length)]} | bot={log.IsBot} {log.BotCategory} | sus={log.IsSuspicious} {log.ThreatType}");
            }

            if (group.Count() > 30)
                sb.AppendLine($"  ... et {group.Count() - 30} autres requêtes");

            sb.AppendLine();
        }

        sb.AppendLine("REPONSE ATTENDUE :");
        sb.AppendLine("Réponds UNIQUEMENT avec un JSON valide, sans markdown, sans commentaire :");
        sb.AppendLine("""
        {
          "analysis": "Résumé court de ton analyse (2-3 phrases)",
          "actions": [
            {"ip": "x.x.x.x", "action": "ban", "reason": "Explication courte du pourquoi"}
          ]
        }
        """);
        sb.AppendLine("Si aucune action n'est nécessaire, retourne un tableau actions vide.");

        return sb.ToString();
    }

    private async Task<string?> CallGeminiAsync(string apiKey, string prompt, CancellationToken ct)
    {
        const int maxRetries = 3;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var requestBody = new
                {
                    contents = new[]
                    {
                        new
                        {
                            role = "user",
                            parts = new[] { new { text = prompt } }
                        }
                    }
                };

                var request = new HttpRequestMessage(HttpMethod.Post, $"{GeminiBaseUrl}?key={apiKey}")
                {
                    Content = JsonContent.Create(requestBody)
                };

                var response = await _httpClient.SendAsync(request, ct);

                // 429 Too Many Requests — attendre et réessayer
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(30 * attempt);
                    _logger.LogWarning("Gemini 429 Rate Limited — retry dans {Seconds}s (tentative {Attempt}/{Max})",
                        retryAfter.TotalSeconds, attempt, maxRetries);
                    await Task.Delay(retryAfter, ct);
                    continue;
                }

                // 500/503 — erreur serveur, réessayer
                if ((int)response.StatusCode >= 500)
                {
                    _logger.LogWarning("Gemini {Status} Server Error — retry dans {Seconds}s (tentative {Attempt}/{Max})",
                        response.StatusCode, 10 * attempt, attempt, maxRetries);
                    await Task.Delay(TimeSpan.FromSeconds(10 * attempt), ct);
                    continue;
                }

                // Autres erreurs (400, 401, 403) — pas de retry
                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync(ct);
                    _logger.LogError("Gemini API erreur {Status}: {Error}", response.StatusCode, error[..Math.Min(500, error.Length)]);
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);

                var text = doc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString();

                _logger.LogDebug("Gemini réponse : {Response}", text?[..Math.Min(200, text?.Length ?? 0)]);
                return text;
            }
            catch (TaskCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur appel Gemini API (tentative {Attempt}/{Max})", attempt, maxRetries);
                if (attempt == maxRetries) return null;
                await Task.Delay(TimeSpan.FromSeconds(5 * attempt), ct);
            }
        }

        return null;
    }

    private List<AiAction> ParseActions(string response)
    {
        try
        {
            // Nettoyer la réponse (Gemini peut ajouter des backticks markdown)
            var cleaned = response.Trim();
            if (cleaned.StartsWith("```"))
            {
                cleaned = cleaned.Split('\n', 2).Last();
                var lastBacktick = cleaned.LastIndexOf("```");
                if (lastBacktick >= 0) cleaned = cleaned[..lastBacktick];
            }

            using var doc = JsonDocument.Parse(cleaned);
            var actions = new List<AiAction>();

            if (doc.RootElement.TryGetProperty("actions", out var actionsArray))
            {
                foreach (var item in actionsArray.EnumerateArray())
                {
                    var ip = item.GetProperty("ip").GetString();
                    var action = item.GetProperty("action").GetString();
                    var reason = item.TryGetProperty("reason", out var r) ? r.GetString() : "AI Analyst";

                    if (!string.IsNullOrEmpty(ip) && !string.IsNullOrEmpty(action))
                    {
                        actions.Add(new AiAction(ip, action, reason ?? "AI Analyst"));
                    }
                }
            }

            if (doc.RootElement.TryGetProperty("analysis", out var analysis))
            {
                _logger.LogInformation("AI Analyst: {Analysis}", analysis.GetString());
            }

            return actions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur parsing réponse Gemini: {Response}", response[..Math.Min(300, response.Length)]);
            return [];
        }
    }

    private async Task BanFromAiAsync(AppDbContext db, string ip, string reason)
    {
        var existing = await db.BannedIps.FirstOrDefaultAsync(b => b.IpAddress == ip);

        if (existing != null)
        {
            if (existing.IsActive) return;

            existing.BanCount++;
            existing.IsActive = true;
            existing.Reason = $"AI: {reason}";
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
                Reason = $"AI: {reason}",
                BannedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                BanCount = 1,
                IsActive = true
            });
        }

        await db.SaveChangesAsync();

        // Sync fichier pour iptables
        try
        {
            const string banFilePath = "/app/Security/banned-ips.txt";
            var ips = await db.BannedIps.Where(b => b.IsActive).Select(b => b.IpAddress).ToListAsync();
            var dir = Path.GetDirectoryName(banFilePath);
            if (dir != null) Directory.CreateDirectory(dir);
            await File.WriteAllLinesAsync(banFilePath, ips);
        }
        catch { /* Best effort */ }
    }

    private record AiAction(string Ip, string Action, string Reason);
}
