using Frpm.Data;
using Frpm.Infrastructure.Providers;
using Microsoft.EntityFrameworkCore;

namespace Frpm.Infrastructure.Security;

[SingletonService(typeof(IProviderCredentialStore))]
public sealed class ProviderCredentialStore(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    CredentialProtector protector) : IProviderCredentialStore
{
    public async Task UpdateAsync(Guid accountId, ProviderCredentials credentials, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var account = await db.ProviderAccounts.SingleOrDefaultAsync(x => x.Id == accountId, cancellationToken);
        if (account is null) return;
        account.ProtectedCredentials = protector.Protect(credentials with { AccountId = null });
        await db.SaveChangesAsync(cancellationToken);
    }
}
