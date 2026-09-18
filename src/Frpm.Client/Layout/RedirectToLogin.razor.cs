namespace Frpm.Client.Layout;

public partial class RedirectToLogin : ComponentBase
{
    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    protected override void OnInitialized()
    {
        var returnUrl = Uri.EscapeDataString(Navigation.ToBaseRelativePath(Navigation.Uri));
        Navigation.NavigateTo($"Account/Login?ReturnUrl={returnUrl}", forceLoad: true);
    }
}
