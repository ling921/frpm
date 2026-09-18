using Frpm.Data;
using Frpm.Domain.Entities;
using Frpm.Domain.Enums;
using Frpm.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Frpm.Infrastructure.Tests;

public sealed class CliPackageActivationPolicyTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly TestDbContextFactory _factory;

    public CliPackageActivationPolicyTests()
    {
        _connection.Open();
        _factory = new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);
    }

    [Fact]
    public async Task FirstAvailablePackageActivatesAutomatically_ButLaterPackageDoesNot()
    {
        await using var db = await _factory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
        var first = Package("1.0.0");
        db.CliPackages.Add(first);
        await db.SaveChangesAsync();

        Assert.True(await CliPackageService.ShouldActivateAsync(_factory, first, false, CancellationToken.None));

        first.IsActive = true;
        var second = Package("1.1.0");
        db.CliPackages.Add(second);
        await db.SaveChangesAsync();

        Assert.False(await CliPackageService.ShouldActivateAsync(_factory, second, false, CancellationToken.None));
        Assert.True(await CliPackageService.ShouldActivateAsync(_factory, second, true, CancellationToken.None));
    }

    private static CliPackage Package(string version) => new()
    {
        ProviderType = ProviderType.LoliaFrp,
        Version = version,
        OperatingSystem = "linux",
        Architecture = "x64",
        Sha256 = new string('a', 64),
        InstallDirectory = $"/data/{version}",
        ExecutablePath = $"/data/{version}/frpc"
    };

    public void Dispose() => _connection.Dispose();

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
