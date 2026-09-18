namespace Frpm.Components.Account.Pages.Manage;

public partial class ChangePassword
{
    [Inject]
    private UserManager<ApplicationUser> UserManager { get; set; } = default!;

    [Inject]
    private SignInManager<ApplicationUser> SignInManager { get; set; } = default!;

    [Inject]
    private IdentityRedirectManager RedirectManager { get; set; } = default!;

    [Inject]
    private ILogger<ChangePassword> Logger { get; set; } = default!;

    private string? message;
    private ApplicationUser? user;
    private bool hasPassword;

    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    [SupplyParameterFromForm]
    private InputModel Input { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        Input ??= new();

        user = await UserManager.GetUserAsync(HttpContext.User);
        if (user is null)
        {
            RedirectManager.RedirectToInvalidUser(UserManager, HttpContext);
            return;
        }

        hasPassword = await UserManager.HasPasswordAsync(user);
        if (!hasPassword)
        {
            RedirectManager.RedirectTo("Account/Manage/SetPassword");
        }
    }

    private async Task OnValidSubmitAsync()
    {
        if (user is null)
        {
            RedirectManager.RedirectToInvalidUser(UserManager, HttpContext);
            return;
        }

        if (await UserManager.CheckPasswordAsync(user, Input.NewPassword))
        {
            message = "错误：新密码不能与当前密码相同。";
            return;
        }

        var mustChangePassword = user.MustChangePassword;
        user.MustChangePassword = false;
        var changePasswordResult = await UserManager.ChangePasswordAsync(user, Input.OldPassword, Input.NewPassword);
        if (!changePasswordResult.Succeeded)
        {
            user.MustChangePassword = mustChangePassword;
            message = $"Error: {string.Join(",", changePasswordResult.Errors.Select(error => error.Description))}";
            return;
        }

        await SignInManager.RefreshSignInAsync(user);
        Logger.LogInformation("User changed their password successfully.");

        RedirectManager.RedirectToWithStatus("", "密码已修改。", HttpContext);
    }

    private sealed class InputModel
    {
        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "Current password")]
        public string OldPassword { get; set; } = string.Empty;

        [Required]
        [StringLength(100, ErrorMessage = "{0}长度必须在 {2} 到 {1} 个字符之间。", MinimumLength = 8)]
        [DataType(DataType.Password)]
        [Display(Name = "New password")]
        public string NewPassword { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Display(Name = "Confirm new password")]
        [Compare("NewPassword", ErrorMessage = "The new password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
