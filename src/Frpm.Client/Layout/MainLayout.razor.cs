namespace Frpm.Client.Layout;

public partial class MainLayout : LayoutComponentBase
{
    [Inject] private IUserPreferencesService PreferencesService { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private bool _drawerOpen = true;
    private bool _isDarkMode;
    private MudThemeProvider? _themeProvider;
    private UserPreferencesModel _preferences = new(
        "auto", "#5468ff", "#52606d", "#8b9cff", "#a8b3cf");
    private MudTheme _theme = BuildTheme(
        "#5468ff", "#52606d", "#8b9cff", "#a8b3cf");
    private string ThemeModeIcon => _preferences.ThemeMode switch
    {
        "light" => Icons.Material.Filled.LightMode,
        "dark" => Icons.Material.Filled.DarkMode,
        _ => Icons.Material.Filled.BrightnessAuto
    };
    private string ThemeToggleTooltip => _preferences.ThemeMode switch
    {
        "light" => "当前：浅色；点击切换到深色",
        "dark" => "当前：深色；点击切换到自动",
        _ => "当前：自动；点击切换到浅色"
    };

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _preferences = await PreferencesService.GetAsync();
            _theme = BuildTheme(
                _preferences.ThemeLightPrimaryColor,
                _preferences.ThemeLightSecondaryColor,
                _preferences.ThemeDarkPrimaryColor,
                _preferences.ThemeDarkSecondaryColor);
            _isDarkMode = _preferences.ThemeMode == "dark";
        }
        catch
        {
            // Keep safe defaults if preferences cannot be loaded during initial navigation.
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && _preferences.ThemeMode == "auto" && _themeProvider is not null)
        {
            _isDarkMode = await _themeProvider.GetSystemDarkModeAsync();
            StateHasChanged();
        }
    }

    private void ToggleDrawer() => _drawerOpen = !_drawerOpen;

    private async Task ToggleThemeAsync()
    {
        var previousPreferences = _preferences;
        var previousIsDarkMode = _isDarkMode;
        var nextMode = _preferences.ThemeMode switch
        {
            "auto" => "light",
            "light" => "dark",
            _ => "auto"
        };
        _preferences = _preferences with { ThemeMode = nextMode };
        _isDarkMode = nextMode switch
        {
            "light" => false,
            "dark" => true,
            _ when _themeProvider is not null => await _themeProvider.GetSystemDarkModeAsync(),
            _ => false
        };
        StateHasChanged();
        var request = new SaveUserPreferencesRequest(
            nextMode,
            _preferences.ThemeLightPrimaryColor,
            _preferences.ThemeLightSecondaryColor,
            _preferences.ThemeDarkPrimaryColor,
            _preferences.ThemeDarkSecondaryColor);
        try
        {
            _preferences = await PreferencesService.SaveAsync(request);
        }
        catch (Exception ex)
        {
            _preferences = previousPreferences;
            _isDarkMode = previousIsDarkMode;
            Snackbar.Add($"切换主题失败：{ex.Message}", Severity.Error);
        }
    }

    private async Task OpenThemePreferencesAsync()
    {
        var parameters = new DialogParameters<ThemePreferencesDialog>
        {
            { component => component.Preferences, _preferences },
            { component => component.EffectiveIsDark, _isDarkMode }
        };
        var dialog = await DialogService.ShowAsync<ThemePreferencesDialog>(
            "外观偏好",
            parameters,
            new DialogOptions { CloseButton = true, MaxWidth = MaxWidth.Small, FullWidth = true });
        var result = await dialog.Result;
        if (result is null || result.Canceled || result.Data is not SaveUserPreferencesRequest request) return;

        await SavePreferencesAsync(request, showConfirmation: true);
    }

    private async Task SavePreferencesAsync(SaveUserPreferencesRequest request, bool showConfirmation)
    {
        try
        {
            _preferences = await PreferencesService.SaveAsync(request);
            _theme = BuildTheme(
                _preferences.ThemeLightPrimaryColor,
                _preferences.ThemeLightSecondaryColor,
                _preferences.ThemeDarkPrimaryColor,
                _preferences.ThemeDarkSecondaryColor);
            _isDarkMode = _preferences.ThemeMode switch
            {
                "dark" => true,
                "light" => false,
                _ when _themeProvider is not null => await _themeProvider.GetSystemDarkModeAsync(),
                _ => false
            };
            if (showConfirmation) Snackbar.Add("外观偏好已保存。", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"保存外观偏好失败：{ex.Message}", Severity.Error);
        }
    }

    private static MudTheme BuildTheme(
        string lightPrimary,
        string lightSecondary,
        string darkPrimary,
        string darkSecondary) => new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = lightPrimary,
            Secondary = lightSecondary,
            Background = "#f6f7fb",
            Surface = "#ffffff"
        },
        PaletteDark = new PaletteDark
        {
            Primary = darkPrimary,
            Secondary = darkSecondary,
            Background = "#11131a",
            Surface = "#191c26"
        },
        LayoutProperties = new LayoutProperties { DefaultBorderRadius = "10px" }
    };

    private static string GetAccountInitial(string? accountName)
    {
        var value = accountName?.Trim();
        return string.IsNullOrEmpty(value)
            ? "?"
            : System.Globalization.StringInfo.GetNextTextElement(value).ToUpperInvariant();
    }
}
