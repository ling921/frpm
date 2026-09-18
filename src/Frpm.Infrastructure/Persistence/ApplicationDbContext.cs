using Frpm.Domain.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Frpm.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<ProviderAccount> ProviderAccounts => Set<ProviderAccount>();
    public DbSet<Tunnel> Tunnels => Set<Tunnel>();
    public DbSet<TunnelRun> TunnelRuns => Set<TunnelRun>();
    public DbSet<CliPackage> CliPackages => Set<CliPackage>();
    public DbSet<SystemConfiguration> SystemConfigurations => Set<SystemConfiguration>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ProviderAccount>(entity =>
        {
            entity.Property(x => x.DisplayName).HasMaxLength(120);
            entity.Property(x => x.ApiBaseUrl).HasMaxLength(500);

            entity.HasIndex(x => new { x.ProviderType, x.DisplayName })
                .IsUnique();
        });

        builder.Entity<Tunnel>(entity =>
        {
            entity.Property(x => x.RemoteId).HasMaxLength(160);
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.Type).HasMaxLength(40);

            entity.HasIndex(x => new { x.ProviderAccountId, x.RemoteId })
                .IsUnique();

            entity.HasOne(x => x.ProviderAccount)
                .WithMany(x => x.Tunnels)
                .HasForeignKey(x => x.ProviderAccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TunnelRun>(entity =>
        {
            entity.Property(x => x.StartedAt).HasConversion(
                value => value.UtcDateTime,
                value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));

            entity.HasOne(x => x.Tunnel)
                .WithMany(x => x.Runs)
                .HasForeignKey(x => x.TunnelId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.CliPackage)
                .WithMany(x => x.Runs)
                .HasForeignKey(x => x.CliPackageId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<CliPackage>(entity =>
        {
            entity.Property(x => x.Version).HasMaxLength(100);
            entity.Property(x => x.OperatingSystem).HasMaxLength(30);
            entity.Property(x => x.Architecture).HasMaxLength(30);
            entity.Property(x => x.InstalledAt).HasConversion(
                value => value.UtcDateTime,
                value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));

            entity.HasIndex(x => new { x.ProviderType, x.Version, x.OperatingSystem, x.Architecture })
                .IsUnique();
        });

        builder.Entity<SystemConfiguration>(entity =>
        {
            entity.Property(x => x.OperatingSystem).HasMaxLength(30);
            entity.Property(x => x.OperatingSystemDescription).HasMaxLength(300);
            entity.Property(x => x.Architecture).HasMaxLength(30);
            entity.Property(x => x.FrameworkDescription).HasMaxLength(100);
            entity.Property(x => x.ApplicationVersion).HasMaxLength(100);
        });

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(x => x.ThemeMode).HasMaxLength(10).HasDefaultValue("auto");
            entity.Property(x => x.ThemePrimaryColor).HasMaxLength(7).HasDefaultValue("#5468ff");
            entity.Property(x => x.ThemeSecondaryColor).HasMaxLength(7).HasDefaultValue("#52606d");
            entity.Property(x => x.ThemeDarkPrimaryColor).HasMaxLength(7).HasDefaultValue("#8b9cff");
            entity.Property(x => x.ThemeDarkSecondaryColor).HasMaxLength(7).HasDefaultValue("#a8b3cf");
        });
    }
}
