namespace Frpm.Components.Account.Pages.Manage;

public partial class TwoFactorAuthentication
{
    [Inject]
    private UserManager<ApplicationUser> UserManager { get; set; } = default!;

    [Inject]
    private SignInManager<ApplicationUser> SignInManager { get; set; } = default!;

    [Inject]
    private IdentityRedirectManager RedirectManager { get; set; } = default!;

    private bool canTrack;
    private bool hasAuthenticator;
    private int recoveryCodesLeft;
    private bool is2faEnabled;
    private bool isMachineRemembered;

    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        var user = await UserManager.GetUserAsync(HttpContext.User);
        if (user is null)
        {
            RedirectManager.RedirectToInvalidUser(UserManager, HttpContext);
            return;
        }

        canTrack = HttpContext.Features.Get<ITrackingConsentFeature>()?.CanTrack ?? true;
        hasAuthenticator = await UserManager.GetAuthenticatorKeyAsync(user) is not null;
        is2faEnabled = await UserManager.GetTwoFactorEnabledAsync(user);
        isMachineRemembered = await SignInManager.IsTwoFactorClientRememberedAsync(user);
        recoveryCodesLeft = await UserManager.CountRecoveryCodesAsync(user);
    }

    private async Task OnSubmitForgetBrowserAsync()
    {
        await SignInManager.ForgetTwoFactorClientAsync();

        RedirectManager.RedirectToCurrentPageWithStatus(
            "The current browser has been forgotten. When you login again from this browser you will be prompted for your 2fa code.",
            HttpContext);
    }
}
