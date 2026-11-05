using Frpm.Abstractions.Models;
using Frpm.Data;
using Frpm.Domain.Entities;
using Frpm.Domain.Enums;
using Frpm.Infrastructure.Providers;
using Frpm.Infrastructure.Security;
using Frpm.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace Frpm.Infrastructure.Tests;

public sealed class LoliaOAuthProtocolTests
{
    [Fact]
    public async Task Authorization_url_contains_lolia_scope_offline_access_and_exact_callback()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var service = new ProviderOAuthService(null!, null!, configuration, null!, null!);

        var result = await service.StartLoliaAsync(new StartLoliaOAuthRequest
        {
            DisplayName = "test",
            ClientId = "client-id",
            CallbackUrl = "https://localhost:7114/oauth/lolia/callback"
        });

        var query = ParseQuery(new Uri(result.AuthorizationUrl).Query);
        Assert.Equal("all", query["scope"]);
        Assert.Equal("offline", query["access_type"]);
        Assert.Equal("https://localhost:7114/oauth/lolia/callback", query["redirect_uri"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.False(string.IsNullOrWhiteSpace(query["state"]));
    }

    [Fact]
    public async Task Existing_account_reuses_encrypted_client_credentials_without_exposing_secret()
    {
        var keyDirectory = Path.Combine(Path.GetTempPath(), $"frpm-test-keys-{Guid.NewGuid():N}");
        Directory.CreateDirectory(keyDirectory);
        try
        {
            using var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            using var contextServices = new ServiceCollection()
                .AddDbContextFactory<ApplicationDbContext>(options => options.UseSqlite(connection))
                .BuildServiceProvider();
            var factory = contextServices.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
            using var dataProtection = new ServiceCollection()
                .AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory))
                .Services
                .BuildServiceProvider();
            var credentialProtector = new CredentialProtector(dataProtection.GetRequiredService<IDataProtectionProvider>());
            var accountId = Guid.NewGuid();

            await using (var db = await factory.CreateDbContextAsync())
            {
                await db.Database.EnsureCreatedAsync();
                db.ProviderAccounts.Add(new ProviderAccount
                {
                    Id = accountId,
                    ProviderType = ProviderType.LoliaFrp,
                    DisplayName = "saved account",
                    ApiBaseUrl = "https://api.lolia.link/api/v1",
                    ProtectedCredentials = credentialProtector.Protect(new ProviderCredentials(
                        "access-token", "refresh-token", "stored-client-id", "one-time-secret"))
                });
                await db.SaveChangesAsync();
            }

            var service = new ProviderOAuthService(
                null!, null!, new ConfigurationBuilder().AddInMemoryCollection().Build(), factory, credentialProtector);
            var result = await service.StartLoliaAsync(new StartLoliaOAuthRequest
            {
                AccountId = accountId,
                DisplayName = "saved account",
                ApiBaseUrl = "https://api.lolia.link/api/v1",
                CallbackUrl = "https://localhost/oauth/lolia/callback"
            });

            var query = ParseQuery(new Uri(result.AuthorizationUrl).Query);
            Assert.Equal("stored-client-id", query["client_id"]);
            Assert.DoesNotContain("one-time-secret", result.AuthorizationUrl, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(keyDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Token_request_sends_client_credentials_in_form_instead_of_basic_auth()
    {
        using var request = LoliaOAuthProtocol.CreateTokenRequest(
            new Uri("https://api.lolia.link/api/v1/oauth2/token"),
            "client-id",
            "client-secret",
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = "code",
                ["code_verifier"] = "verifier"
            });

        Assert.Null(request.Headers.Authorization);
        var form = ParseQuery(await request.Content!.ReadAsStringAsync());
        Assert.Equal("client-id", form["client_id"]);
        Assert.Equal("client-secret", form["client_secret"]);
        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal("verifier", form["code_verifier"]);
    }

    private static Dictionary<string, string> ParseQuery(string value) => value
        .TrimStart('?')
        .Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => part.Split('=', 2))
        .ToDictionary(
            part => Uri.UnescapeDataString(part[0].Replace('+', ' ')),
            part => Uri.UnescapeDataString((part.Length > 1 ? part[1] : "").Replace('+', ' ')));
}
