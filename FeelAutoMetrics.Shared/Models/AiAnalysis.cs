using System;

namespace FeelAutoMetrics.Shared.Models;

public class AiAnalysis
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset AnalyzedAt { get; set; } = DateTimeOffset.UtcNow;
    public int LogsAnalyzed { get; set; }
    public int ActionsTaken { get; set; }
    public string? Summary { get; set; }
    public DateTimeOffset LogsFromTimestamp { get; set; }
    public DateTimeOffset LogsToTimestamp { get; set; }
}
