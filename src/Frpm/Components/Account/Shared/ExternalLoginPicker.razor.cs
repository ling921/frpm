namespace Frpm.Components.Account.Shared;

public partial class ExternalLoginPicker
{
    [Inject]
    private SignInManager<ApplicationUser> SignInManager { get; set; } = default!;

    [Inject]
    private IdentityRedirectManager RedirectManager { get; set; } = default!;

    private AuthenticationScheme[] externalLogins = [];

    [SupplyParameterFromQuery]
    private string? ReturnUrl { get; set; }

    protected override async Task OnInitializedAsync()
    {
        externalLogins = (await SignInManager.GetExternalAuthenticationSchemesAsync()).ToArray();
    }
}
