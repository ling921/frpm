using Microsoft.AspNetCore.Identity;

namespace Frpm.Data;

public sealed class ApplicationUser : IdentityUser
{
    public bool MustChangePassword { get; set; }
    public string ThemeMode { get; set; } = "auto";
    public string ThemePrimaryColor { get; set; } = "#5468ff";
    public string ThemeSecondaryColor { get; set; } = "#52606d";
    public string ThemeDarkPrimaryColor { get; set; } = "#8b9cff";
    public string ThemeDarkSecondaryColor { get; set; } = "#a8b3cf";
}
