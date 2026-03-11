using System.Text.Json;
using FeelAutoMetrics.Shared.Models;

namespace FeelAutoMetrics.Infrastructure.Services;

public class LogProcessor : ILogProcessor
{
    private readonly IGeoIpService _geoIpService;
    private readonly IUserAgentService _userAgentService;
    private readonly ISecurityService _securityService;

    public LogProcessor(IGeoIpService geoIpService, IUserAgentService userAgentService, ISecurityService securityService)
    {
        _geoIpService = geoIpService;
        _userAgentService = userAgentService;
        _securityService = securityService;
    }

    public async Task<GlobalAccessLog> ProcessAsync(string rawJson)
    {
        var dto = JsonSerializer.Deserialize<TraefikLogDto>(rawJson);
        if (dto == null) throw new InvalidOperationException("Failed to deserialize Traefik log JSON.");

        var log = new GlobalAccessLog
        {
            Timestamp = dto.Timestamp,
            ClientHost = dto.ClientHost,
            ClientPort = dto.ClientPort,
            RequestMethod = dto.RequestMethod,
            RequestPath = dto.RequestPath,
            RequestProtocol = dto.RequestProtocol,
            RequestScheme = dto.RequestScheme,
            RequestPort = dto.RequestPort,
            RequestHost = dto.RequestHost,
            ResponseStatusCode = dto.DownstreamStatus,
            ResponseContentSize = dto.DownstreamContentSize,
            DurationMs = dto.Duration / 1_000_000, // Conversion nanosecondes -> millisecondes
            OriginDurationMs = dto.OriginDuration / 1_000_000,
            OverheadMs = dto.Overhead / 1_000_000,
            RouterName = dto.RouterName,
            ServiceName = dto.ServiceName,
            UserAgentBrut = dto.UserAgent ?? string.Empty
        };

        // GeoIP Enrichment
        var (countryCode, countryName, cityName, lat, lon) = _geoIpService.GetLocation(log.ClientHost);
        log.CountryCode = countryCode;
        log.CountryName = countryName;
        log.CityName = cityName;
        log.Latitude = lat;
        log.Longitude = lon;

        // User-Agent Enrichment
        var (browser, browserVersion, os, osVersion, device, isBot, botCategory) = _userAgentService.Parse(log.UserAgentBrut);
        log.BrowserName = browser;
        log.BrowserVersion = browserVersion;
        log.OsName = os;
        log.OsVersion = osVersion;
        log.DeviceFamily = device;
        log.IsBot = isBot;
        log.BotCategory = botCategory;

        // Security Analysis
        await _securityService.AnalyzeLogAsync(log);

        return log;
    }
}
