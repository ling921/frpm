namespace Frpm.Components.Account.Pages;

public partial class Login
{
    [Inject]
    private UserManager<ApplicationUser> UserManager { get; set; } = default!;

    [Inject]
    private SignInManager<ApplicationUser> SignInManager { get; set; } = default!;

    [Inject]
    private ILogger<Login> Logger { get; set; } = default!;

    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    private IdentityRedirectManager RedirectManager { get; set; } = default!;

    private string? errorMessage;
    private EditContext editContext = default!;
    private static Dictionary<string, object> AccountInputAttributes { get; } =
        new Dictionary<string, object> { ["name"] = "Input.Account" };
    private static Dictionary<string, object> PasswordInputAttributes { get; } =
        new Dictionary<string, object> { ["name"] = "Input.Password" };

    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    [SupplyParameterFromForm]
    private InputModel Input { get; set; } = default!;

    [SupplyParameterFromQuery]
    private string? ReturnUrl { get; set; }

    protected override async Task OnInitializedAsync()
    {
        Input ??= new();

        editContext = new EditContext(Input);

        if (HttpMethods.IsGet(HttpContext.Request.Method))
        {
            // Clear the existing external cookie to ensure a clean login process
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        }
    }

    public async Task LoginUser()
    {
        if (!string.IsNullOrEmpty(Input.Passkey?.Error))
        {
            errorMessage = $"Error: {Input.Passkey.Error}";
            return;
        }

        SignInResult result;
        if (!string.IsNullOrEmpty(Input.Passkey?.CredentialJson))
        {
            // When performing passkey sign-in, don't perform form validation.
            result = await SignInManager.PasskeySignInAsync(Input.Passkey.CredentialJson);
        }
        else
        {
            // If doing a password sign-in, validate the form.
            if (!editContext.Validate())
            {
                return;
            }

            // This doesn't count login failures towards account lockout
            // To enable password failures to trigger account lockout, set lockoutOnFailure: true
            result = await SignInManager.PasswordSignInAsync(Input.Account, Input.Password, Input.RememberMe, lockoutOnFailure: false);
        }

        if (result.Succeeded)
        {
            Logger.LogInformation("User logged in.");
            RedirectManager.RedirectTo(ReturnUrl);
        }
        else if (result.RequiresTwoFactor)
        {
            RedirectManager.RedirectTo(
                "Account/LoginWith2fa",
                new() { ["returnUrl"] = ReturnUrl, ["rememberMe"] = Input.RememberMe });
        }
        else if (result.IsLockedOut)
        {
            Logger.LogWarning("User account locked out.");
            RedirectManager.RedirectTo("Account/Lockout");
        }
        else
        {
            errorMessage = "错误：账号或密码不正确。";
        }
    }

    private sealed class InputModel
    {
        [Required(ErrorMessage = "请输入账号。")]
        [Display(Name = "账号")]
        public string Account { get; set; } = string.Empty;

        [Required(ErrorMessage = "请输入密码。")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Remember me?")]
        public bool RememberMe { get; set; }

        public PasskeyInputModel? Passkey { get; set; }
    }
}
