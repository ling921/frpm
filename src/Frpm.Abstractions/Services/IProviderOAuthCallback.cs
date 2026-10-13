namespace Frpm.Abstractions.Services;

/// <summary>Completes browser authorization in either the desktop or Web host.</summary>
public interface IProviderOAuthCallback
{
    Task CompleteLoliaAsync(string code, string state, CancellationToken cancellationToken = default);
    void CancelLolia(string state);
}
