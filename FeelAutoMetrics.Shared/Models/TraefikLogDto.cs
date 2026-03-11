using System;
using System.Text.Json.Serialization;

namespace FeelAutoMetrics.Shared.Models;

public class TraefikLogDto
{
    [JsonPropertyName("time")]
    public DateTimeOffset Timestamp { get; set; }

    [JsonPropertyName("ClientHost")]
    public string ClientHost { get; set; } = string.Empty;

    [JsonPropertyName("ClientPort")]
    public string? ClientPort { get; set; }

    [JsonPropertyName("RequestMethod")]
    public string RequestMethod { get; set; } = string.Empty;

    [JsonPropertyName("RequestPath")]
    public string RequestPath { get; set; } = string.Empty;

    [JsonPropertyName("RequestProtocol")]
    public string RequestProtocol { get; set; } = string.Empty;

    [JsonPropertyName("RequestScheme")]
    public string RequestScheme { get; set; } = string.Empty;

    [JsonPropertyName("RequestPort")]
    public string? RequestPort { get; set; }

    [JsonPropertyName("RequestHost")]
    public string RequestHost { get; set; } = string.Empty;

    [JsonPropertyName("DownstreamStatus")]
    public int DownstreamStatus { get; set; }

    [JsonPropertyName("DownstreamContentSize")]
    public long DownstreamContentSize { get; set; }

    [JsonPropertyName("Duration")]
    public long Duration { get; set; } // En nanosecondes généralement par Traefik

    [JsonPropertyName("OriginDuration")]
    public long? OriginDuration { get; set; }

    [JsonPropertyName("Overhead")]
    public long? Overhead { get; set; }

    [JsonPropertyName("RouterName")]
    public string? RouterName { get; set; }

    [JsonPropertyName("ServiceName")]
    public string? ServiceName { get; set; }

    [JsonPropertyName("RequestAddr")]
    public string? RequestAddr { get; set; }

    [JsonPropertyName("request_User-Agent")]
    public string? UserAgent { get; set; }
}
