namespace Frpm.Components.Account.Pages;

public partial class Register
{
    [Inject]
    private IdentityRedirectManager RedirectManager { get; set; } = default!;

    protected override void OnInitialized() => RedirectManager.RedirectTo("Account/Login");
}
