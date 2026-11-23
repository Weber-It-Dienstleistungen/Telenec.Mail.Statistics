using Microsoft.EntityFrameworkCore;
using Telenec.Mail.Statistics.Server.Models;
using Telenec.Mail.Statistics.Server.MailSecurity;

namespace Telenec.Mail.Statistics.Server.Data;

public sealed class StatisticsDbContext : DbContext
{
    public StatisticsDbContext(DbContextOptions<StatisticsDbContext> options)
        : base(options)
    {
    }

    public DbSet<Installation> Installations => Set<Installation>();
    public DbSet<MailSecurityCheckpointState> MailSecurityCheckpointStates =>
        Set<MailSecurityCheckpointState>();
    public DbSet<MailSecurityIncident> MailSecurityIncidents =>
        Set<MailSecurityIncident>();

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

        // Beobachtende Spamerkennung, getrennt vom anonymisierten
        // Installations-/Nutzungsstatistik-Modell.
        modelBuilder.Entity<MailSecurityCheckpointState>(entity =>
        {
            entity.ToTable("MailSecurityCheckpointStates");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.CheckpointJson).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).IsRequired();
        });

        modelBuilder.Entity<MailSecurityIncident>(entity =>
        {
            entity.ToTable("MailSecurityIncidents");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Account)
                .HasMaxLength(254).IsRequired();
            entity.Property(x => x.FirstObservedUtc).IsRequired();
            entity.Property(x => x.LastObservedUtc).IsRequired();
            entity.Property(x => x.NewPassed).IsRequired();
            entity.Property(x => x.NewRejected).IsRequired();
            entity.Property(x => x.MaxRiskLevel).IsRequired();
            entity.Property(x => x.SendingLimitZero).IsRequired();

            // Hoestens EIN offener Fall pro Account, auch wenn
            // spaeter versehentlich mehrere Worker schreiben.
            entity.HasIndex(x => x.Account)
                .IsUnique()
                .HasFilter("\"ClosedAtUtc\" IS NULL")
                .HasDatabaseName("IX_MailSecurityIncidents_OpenAccount");
            entity.HasIndex(x => x.ClosedAtUtc);
        });
    }
}