using Frpm.Infrastructure.Providers;
using Frpm.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;

namespace Frpm.Infrastructure.Tests;

public sealed class CredentialProtectorTests
{
    [Fact]
    public void Credentials_are_encrypted_and_round_trip()
    {
        var protector = new CredentialProtector(new EphemeralDataProtectionProvider());
        var encrypted = protector.Protect(new ProviderCredentials("access", "refresh", "client", "secret"));

        Assert.DoesNotContain("access", encrypted);
        var restored = protector.Unprotect(encrypted);
        Assert.Equal("access", restored.AccessToken);
        Assert.Equal("refresh", restored.RefreshToken);
    }
}
