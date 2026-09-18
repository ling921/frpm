using Frpm.Infrastructure.Providers;
using Microsoft.AspNetCore.DataProtection;
using System.Text.Json;

namespace Frpm.Infrastructure.Security;

[SingletonService]
public sealed class CredentialProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("Frpm.ProviderCredentials.v1");

    public string Protect(ProviderCredentials credentials) => _protector.Protect(JsonSerializer.Serialize(credentials));

    public ProviderCredentials Unprotect(string value) =>
        JsonSerializer.Deserialize<ProviderCredentials>(_protector.Unprotect(value))
        ?? throw new InvalidOperationException("供应商凭据格式无效。");
}
