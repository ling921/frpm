namespace Frpm.Abstractions.Models;

public sealed record UserPreferencesModel(
    string ThemeMode,
    string ThemeLightPrimaryColor,
    string ThemeLightSecondaryColor,
    string ThemeDarkPrimaryColor,
    string ThemeDarkSecondaryColor);

public sealed record SaveUserPreferencesRequest(
    string ThemeMode,
    string ThemeLightPrimaryColor,
    string ThemeLightSecondaryColor,
    string ThemeDarkPrimaryColor,
    string ThemeDarkSecondaryColor);
