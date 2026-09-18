namespace Frpm.Components.Account.Shared;

public partial class AccountLayout
{
    private static readonly MudTheme Theme = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#5468ff",
            Secondary = "#52606d",
            Background = "#f6f7fb",
            Surface = "#ffffff"
        },
        LayoutProperties = new LayoutProperties { DefaultBorderRadius = "10px" }
    };
}
