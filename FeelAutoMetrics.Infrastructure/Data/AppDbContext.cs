using FeelAutoMetrics.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace FeelAutoMetrics.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<GlobalAccessLog> GlobalAccessLogs { get; set; }
    public DbSet<AppEvent> AppEvents { get; set; }
    public DbSet<BannedIp> BannedIps { get; set; }
    public DbSet<ExcludedIp> ExcludedIps { get; set; }
    public DbSet<IpThreatScore> IpThreatScores { get; set; }
    public DbSet<AiAnalysis> AiAnalyses { get; set; }
    public DbSet<SystemSetting> SystemSettings { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configuration SystemSetting
        modelBuilder.Entity<SystemSetting>(entity =>
        {
            entity.HasKey(e => e.Key);
            entity.Property(e => e.Key).HasMaxLength(100);
            entity.Property(e => e.Value).IsRequired();
        });

        // Configuration GlobalAccessLog (déjà présente)
        modelBuilder.Entity<GlobalAccessLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.RequestHost);
            entity.HasIndex(e => e.ClientHost);
            entity.HasIndex(e => e.CountryCode);
            entity.HasIndex(e => e.ResponseStatusCode);
            entity.Property(e => e.RequestHost).IsRequired().HasMaxLength(255);
            entity.Property(e => e.ClientHost).IsRequired().HasMaxLength(128);
            entity.Property(e => e.RequestMethod).IsRequired().HasMaxLength(16);
            entity.Property(e => e.RequestPath).IsRequired().HasMaxLength(2048);
        });

        // Configuration BannedIp
        modelBuilder.Entity<BannedIp>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.IpAddress).IsUnique();
            entity.Property(e => e.IpAddress).IsRequired().HasMaxLength(45);
        });

        // Configuration IpThreatScore
        modelBuilder.Entity<IpThreatScore>(entity =>
        {
            entity.HasKey(e => e.IpAddress);
            entity.Property(e => e.IpAddress).HasMaxLength(45);
        });

        // Configuration ExcludedIp
        modelBuilder.Entity<ExcludedIp>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.IpAddress).IsUnique();
            entity.Property(e => e.IpAddress).IsRequired().HasMaxLength(45);
            entity.Property(e => e.Label).HasMaxLength(100);
        });

        // Configuration AiAnalysis
        modelBuilder.Entity<AiAnalysis>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.AnalyzedAt);
            entity.Property(e => e.Summary).HasMaxLength(4000);
        });

        // Configuration AppEvent
        modelBuilder.Entity<AppEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.AppName);
            entity.HasIndex(e => e.Level);
            entity.HasIndex(e => e.CorrelationId);
            entity.HasIndex(e => e.UserId);

            entity.Property(e => e.AppName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Level).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Message).IsRequired().HasMaxLength(4000);
            
            // PostgreSQL JSONB support for Dictionary
            entity.Property(e => e.Metadata).HasColumnType("jsonb");
        });
    }
}
