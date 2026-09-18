using Frpm.Abstractions.Models;
using Frpm.Abstractions.Services;
using Frpm.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace Frpm.Infrastructure.Services;

[ScopedService(typeof(IUserPreferencesService))]
public sealed partial class UserPreferencesService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IHttpContextAccessor httpContextAccessor,
    AuthenticationStateProvider authenticationStateProvider) : IUserPreferencesService
{
    public async Task<UserPreferencesModel> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var user = await GetCurrentUserAsync(db, cancellationToken);
        return Map(user);
    }

    public async Task<UserPreferencesModel> SaveAsync(
        SaveUserPreferencesRequest request,
        CancellationToken cancellationToken = default)
    {
        var mode = request.ThemeMode.Trim().ToLowerInvariant();
        if (mode is not ("auto" or "light" or "dark"))
            throw new InvalidOperationException("主题模式必须是自动、浅色或深色。");
        var lightPrimary = NormalizeColor(request.ThemeLightPrimaryColor, "浅色主题主色");
        var lightSecondary = NormalizeColor(request.ThemeLightSecondaryColor, "浅色主题辅助色");
        var darkPrimary = NormalizeColor(request.ThemeDarkPrimaryColor, "深色主题主色");
        var darkSecondary = NormalizeColor(request.ThemeDarkSecondaryColor, "深色主题辅助色");

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var user = await GetCurrentUserAsync(db, cancellationToken);
        user.ThemeMode = mode;
        user.ThemePrimaryColor = lightPrimary;
        user.ThemeSecondaryColor = lightSecondary;
        user.ThemeDarkPrimaryColor = darkPrimary;
        user.ThemeDarkSecondaryColor = darkSecondary;
        await db.SaveChangesAsync(cancellationToken);
        return Map(user);
    }

    private async Task<ApplicationUser> GetCurrentUserAsync(
        ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated is not true)
            principal = (await authenticationStateProvider.GetAuthenticationStateAsync()).User;
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("用户尚未登录。");
        return await db.Users.SingleOrDefaultAsync(user => user.Id == userId, cancellationToken)
            ?? throw new UnauthorizedAccessException("当前用户不存在。");
    }

    private static string NormalizeColor(string value, string label)
    {
        var color = value.Trim();
        if (!HexColorRegex().IsMatch(color))
            throw new InvalidOperationException($"{label}必须是 #RRGGBB 格式。");
        return color.ToLowerInvariant();
    }

    private static UserPreferencesModel Map(ApplicationUser user) => new(
        user.ThemeMode,
        user.ThemePrimaryColor,
        user.ThemeSecondaryColor,
        user.ThemeDarkPrimaryColor,
        user.ThemeDarkSecondaryColor);

    [GeneratedRegex("^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexColorRegex();
}
