using System.Text.RegularExpressions;

namespace Frpm.Client.Components;

public partial class ThemePreferencesDialog : ComponentBase
{
    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = default!;
    [Parameter, EditorRequired] public UserPreferencesModel Preferences { get; set; } = default!;
    [Parameter] public bool EffectiveIsDark { get; set; }

    private string _themeMode = "auto";
    private string _lightPrimaryColor = "#5468ff";
    private string _lightSecondaryColor = "#52606d";
    private string _darkPrimaryColor = "#8b9cff";
    private string _darkSecondaryColor = "#a8b3cf";
    private string? _error;
    private bool EditingDark => _themeMode == "dark" || (_themeMode == "auto" && EffectiveIsDark);
    private string CurrentPrimaryColor
    {
        get => EditingDark ? _darkPrimaryColor : _lightPrimaryColor;
        set { if (EditingDark) _darkPrimaryColor = value; else _lightPrimaryColor = value; }
    }
    private string CurrentSecondaryColor
    {
        get => EditingDark ? _darkSecondaryColor : _lightSecondaryColor;
        set { if (EditingDark) _darkSecondaryColor = value; else _lightSecondaryColor = value; }
    }

    protected override void OnInitialized()
    {
        _themeMode = Preferences.ThemeMode;
        _lightPrimaryColor = Preferences.ThemeLightPrimaryColor;
        _lightSecondaryColor = Preferences.ThemeLightSecondaryColor;
        _darkPrimaryColor = Preferences.ThemeDarkPrimaryColor;
        _darkSecondaryColor = Preferences.ThemeDarkSecondaryColor;
    }

    private void Save()
    {
        if (!Regex.IsMatch(_lightPrimaryColor, "^#[0-9a-fA-F]{6}$")
            || !Regex.IsMatch(_lightSecondaryColor, "^#[0-9a-fA-F]{6}$")
            || !Regex.IsMatch(_darkPrimaryColor, "^#[0-9a-fA-F]{6}$")
            || !Regex.IsMatch(_darkSecondaryColor, "^#[0-9a-fA-F]{6}$"))
        {
            _error = "主题颜色必须使用 #RRGGBB 格式。";
            return;
        }

        Dialog.Close(DialogResult.Ok(new SaveUserPreferencesRequest(
            _themeMode,
            _lightPrimaryColor,
            _lightSecondaryColor,
            _darkPrimaryColor,
            _darkSecondaryColor)));
    }

    private void Cancel() => Dialog.Cancel();
}
