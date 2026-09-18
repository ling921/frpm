namespace Frpm.Components.Account.Shared;

public partial class ManageNavMenu
{
    [Inject]
    private SignInManager<ApplicationUser> SignInManager { get; set; } = default!;

    private bool hasExternalLogins;

    protected override async Task OnInitializedAsync()
    {
        hasExternalLogins = (await SignInManager.GetExternalAuthenticationSchemesAsync()).Any();
    }
}
