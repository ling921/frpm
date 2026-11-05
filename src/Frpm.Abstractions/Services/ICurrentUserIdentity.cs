namespace Frpm.Abstractions.Services;

/// <summary>Provides the local or authenticated user without depending on a UI framework.</summary>
public interface ICurrentUserIdentity
{
    Task<string?> GetUserIdAsync(CancellationToken cancellationToken = default);
}
