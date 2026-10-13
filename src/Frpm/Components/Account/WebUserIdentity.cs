using System.Security.Claims;
using Frpm.Abstractions.Services;

namespace Frpm.Components.Account;

internal sealed class WebUserIdentity(
    IHttpContextAccessor httpContextAccessor,
    AuthenticationStateProvider authenticationStateProvider) : ICurrentUserIdentity
{
    public async Task<string?> GetUserIdAsync(CancellationToken cancellationToken = default)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated is not true)
            principal = (await authenticationStateProvider.GetAuthenticationStateAsync()).User;
        return principal.Identity?.IsAuthenticated is true
            ? principal.FindFirstValue(ClaimTypes.NameIdentifier)
            : null;
    }
}
