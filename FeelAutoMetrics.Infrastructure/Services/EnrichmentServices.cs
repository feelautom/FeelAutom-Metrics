using FeelAutoMetrics.Shared.Models;
using MaxMind.GeoIP2;
using UAParser;

namespace FeelAutoMetrics.Infrastructure.Services;

public interface ILogProcessor
{
    Task<GlobalAccessLog> ProcessAsync(string rawJson);
}

public interface IGeoIpService
{
    (string? CountryCode, string? CountryName, string? CityName, double? Latitude, double? Longitude) GetLocation(string ipAddress);
}

public interface IUserAgentService
{
    (string? Browser, string? Version, string? Os, string? OsVersion, string? Device, bool IsBot, string? BotCategory) Parse(string userAgent);
}

public class UserAgentService : IUserAgentService
{
    private readonly Parser _parser;

    private static readonly string[] GoodBotPatterns =
    [
        "Googlebot", "Bingbot", "Applebot", "DuckDuckBot", "Slurp",
        "facebookexternalhit", "LinkedInBot", "Twitterbot",
        "Amazonbot", "YandexBot", "Baiduspider", "SemrushBot",
        "AhrefsBot", "MJ12bot", "PetalBot", "Bytespider"
    ];

    public UserAgentService()
    {
        _parser = Parser.GetDefault();
    }

    public (string? Browser, string? Version, string? Os, string? OsVersion, string? Device, bool IsBot, string? BotCategory) Parse(string userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return (null, null, null, null, null, false, null);

        var client = _parser.Parse(userAgent);

        // Check for known good bots first
        foreach (var pattern in GoodBotPatterns)
        {
            if (userAgent.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return (
                    client.UA.Family,
                    $"{client.UA.Major}.{client.UA.Minor}",
                    client.OS.Family,
                    $"{client.OS.Major}.{client.OS.Minor}",
                    client.Device.Family,
                    true,
                    "GoodBot"
                );
            }
        }

        // Check for generic bot indicators
        bool isBot = userAgent.Contains("bot", StringComparison.OrdinalIgnoreCase) ||
                     userAgent.Contains("spider", StringComparison.OrdinalIgnoreCase) ||
                     userAgent.Contains("crawl", StringComparison.OrdinalIgnoreCase);

        return (
            client.UA.Family,
            $"{client.UA.Major}.{client.UA.Minor}",
            client.OS.Family,
            $"{client.OS.Major}.{client.OS.Minor}",
            client.Device.Family,
            isBot,
            isBot ? "UnknownBot" : null
        );
    }
}

public class GeoIpService : IGeoIpService
{
    private readonly string? _dbPath;
    private readonly DatabaseReader? _reader;

    public GeoIpService(string? dbPath = null)
    {
        _dbPath = dbPath;
        if (!string.IsNullOrEmpty(_dbPath) && File.Exists(_dbPath))
        {
            _reader = new DatabaseReader(_dbPath);
        }
    }

    public (string? CountryCode, string? CountryName, string? CityName, double? Latitude, double? Longitude) GetLocation(string ipAddress)
    {
        if (_reader == null) return (null, null, null, null, null);

        try
        {
            var response = _reader.City(ipAddress);
            return (
                response.Country.IsoCode,
                response.Country.Name,
                response.City.Name,
                response.Location.Latitude,
                response.Location.Longitude
            );
        }
        catch
        {
            return (null, null, null, null, null);
        }
    }
}
