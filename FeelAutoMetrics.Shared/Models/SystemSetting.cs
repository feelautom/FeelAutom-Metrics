using System;
using System.ComponentModel.DataAnnotations;

namespace FeelAutoMetrics.Shared.Models;

public class SystemSetting
{
    [Key]
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTimeOffset LastModified { get; set; } = DateTimeOffset.UtcNow;
}
