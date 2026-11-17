using System.Security.Claims;
using Frpm.Abstractions.Services;
using Microsoft.AspNetCore.Components.Authorization;

namespace Frpm.Components.Account;

internal sealed class WebUserIdentity : ICurrentUserIdentity, IDisposable
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly AuthenticationStateProvider _authenticationStateProvider;
    private Task<AuthenticationState>? _currentAuthStateTask;

    public WebUserIdentity(
        IHttpContextAccessor httpContextAccessor,
        AuthenticationStateProvider authenticationStateProvider)
    {
        _httpContextAccessor = httpContextAccessor;
        _authenticationStateProvider = authenticationStateProvider;
        _authenticationStateProvider.AuthenticationStateChanged += OnAuthenticationStateChanged;
    }

    private void OnAuthenticationStateChanged(Task<AuthenticationState> task)
    {
        _currentAuthStateTask = task;
    }

    public async Task<string?> GetUserIdAsync(CancellationToken cancellationToken = default)
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated is not true)
        {
            var task = _currentAuthStateTask ?? _authenticationStateProvider.GetAuthenticationStateAsync();
            principal = (await task).User;
        }

        return principal.Identity?.IsAuthenticated is true
            ? principal.FindFirstValue(ClaimTypes.NameIdentifier)
            : null;
    }

    public void Dispose()
    {
        _authenticationStateProvider.AuthenticationStateChanged -= OnAuthenticationStateChanged;
    }
}
