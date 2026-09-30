using Microsoft.EntityFrameworkCore;
using Telenec.Mail.Statistics.Server.Models;

namespace Telenec.Mail.Statistics.Server.Data;

public sealed class StatisticsDbContext : DbContext
{
    public StatisticsDbContext(DbContextOptions<StatisticsDbContext> options)
        : base(options)
    {
    }

    public DbSet<Installation> Installations => Set<Installation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Installation>(entity =>
        {
            entity.ToTable("Installations");

            entity.HasKey(x => x.InstallationKey);

            entity.Property(x => x.InstallationKey)
                .HasMaxLength(64)
                .IsRequired();

            entity.Property(x => x.FirstSeenUtc)
                .IsRequired();

            entity.Property(x => x.LastSeenUtc)
                .IsRequired();

            entity.Property(x => x.CurrentVersion)
                .HasMaxLength(64)
                .IsRequired();

            entity.Property(x => x.ConsentVersion)
                .IsRequired();

            entity.HasIndex(x => x.LastSeenUtc);

            entity.HasIndex(x => x.FirstSeenUtc);

            entity.HasIndex(x => x.CurrentVersion);
        });
    }
}