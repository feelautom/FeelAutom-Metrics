using System;

namespace FeelAutoMetrics.Shared.Models;

public class GlobalAccessLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; set; }

    // Traefik Raw Fields
    public string ClientHost { get; set; } = string.Empty;
    public string? ClientPort { get; set; }
    public string RequestMethod { get; set; } = string.Empty;
    public string RequestPath { get; set; } = string.Empty;
    public string RequestProtocol { get; set; } = string.Empty;
    public string RequestScheme { get; set; } = string.Empty;
    public string? RequestPort { get; set; }
    public string RequestHost { get; set; } = string.Empty; // Le domaine cible (ex: sipline.feelautom.fr)
    public int ResponseStatusCode { get; set; }
    public long ResponseContentSize { get; set; }
    public long DurationMs { get; set; }
    public long? OriginDurationMs { get; set; }
    public long? OverheadMs { get; set; }
    
    public string? RouterName { get; set; }
    public string? ServiceName { get; set; }
    public string UserAgentBrut { get; set; } = string.Empty;

    // Enriched Fields: GeoIP
    public string? CountryCode { get; set; }
    public string? CountryName { get; set; }
    public string? CityName { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    // Enriched Fields: User-Agent
    public string? BrowserName { get; set; }
    public string? BrowserVersion { get; set; }
    public string? OsName { get; set; }
    public string? OsVersion { get; set; }
    public string? DeviceFamily { get; set; }
    public bool IsBot { get; set; }
    public string? BotCategory { get; set; }
}
