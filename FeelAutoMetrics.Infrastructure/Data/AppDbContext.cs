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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

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
