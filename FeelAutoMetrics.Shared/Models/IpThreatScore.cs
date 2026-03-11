using System;
using System.ComponentModel.DataAnnotations;

namespace FeelAutoMetrics.Shared.Models;

public class IpThreatScore
{
    [Key]
    public string IpAddress { get; set; } = string.Empty;
    public int Score { get; set; }
    public DateTimeOffset LastHit { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset FirstSeen { get; set; } = DateTimeOffset.UtcNow;
}
