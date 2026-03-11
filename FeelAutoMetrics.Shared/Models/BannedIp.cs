using System;

namespace FeelAutoMetrics.Shared.Models;

public class BannedIp
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string IpAddress { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public DateTimeOffset BannedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; set; } // null = permanent

    public bool IsExpired => ExpiresAt.HasValue && ExpiresAt.Value < DateTimeOffset.UtcNow;
}
