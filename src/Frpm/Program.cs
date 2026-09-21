using Frpm.Api;
using Frpm.Components;
using Frpm.Components.Account;
using Frpm.Generated;
using Frpm.Infrastructure.Extensions;
using Ling.RemoteServices.AspNetCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting.WindowsServices;
using MudBlazor.Services;

var resetAdminPassword = args.Any(argument => string.Equals(argument, "--reset-admin-password", StringComparison.OrdinalIgnoreCase));
var serviceMode = OperatingSystem.IsWindows() && WindowsServiceHelpers.IsWindowsService();
var useWindowsInstallationDataDirectory = OperatingSystem.IsWindows() && (serviceMode || resetAdminPassword);

if (useWindowsInstallationDataDirectory)
{
    var serviceDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "FRPM");
    Directory.CreateDirectory(serviceDataDirectory);
    Directory.SetCurrentDirectory(serviceDataDirectory);
}

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args.Where(argument => !string.Equals(argument, "--reset-admin-password", StringComparison.OrdinalIgnoreCase)).ToArray(),
    ContentRootPath = useWindowsInstallationDataDirectory ? AppContext.BaseDirectory : null
});

if (useWindowsInstallationDataDirectory)
{
    builder.Host.UseWindowsService();
    builder.Configuration.AddJsonFile(
        Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"),
        optional: true,
        reloadOnChange: true)
        .AddJsonFile(
            Path.Combine(Directory.GetCurrentDirectory(), "appsettings.Production.json"),
            optional: true,
            reloadOnChange: true);
}

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Add services to the container.
builder.Services.AddMudServices();
builder.Services.AddFrpmInfrastructure(builder.Configuration);
builder.Services.AddRemoteServices();
builder.Services.AddHealthChecks();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents()
    .AddAuthenticationStateSerialization();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.User.RequireUniqueEmail = true;
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();
builder.Services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, ApplicationUserClaimsPrincipalFactory>();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();
    await InitialAdminSeeder.SeedAsync(
        scope.ServiceProvider,
        scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("InitialAdminSeeder"));

    if (resetAdminPassword)
    {
        var temporaryPassword = await InitialAdminSeeder.ResetPasswordAsync(
            scope.ServiceProvider,
            scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("InitialAdminSeeder"));
        Console.WriteLine($"FRPM administrator password reset. Username: {InitialAdminSeeder.UserName}; Temporary password: {temporaryPassword}");
        return;
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
//app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.Use(async (context, next) =>
{
    var mustChangePassword = context.User.HasClaim(
        ApplicationUserClaimsPrincipalFactory.MustChangePasswordClaim,
        bool.TrueString);
    var path = context.Request.Path;
    var allowedPath = path.StartsWithSegments("/Account/Manage/ChangePassword")
        || path.StartsWithSegments("/Account/Logout")
        || path.StartsWithSegments("/_framework")
        || path.StartsWithSegments("/_content")
        || Path.HasExtension(path.Value);

    if (mustChangePassword && !allowedPath)
    {
        if (HttpMethods.IsGet(context.Request.Method)
            && context.Request.GetTypedHeaders().Accept?.Any(x => x.MediaType == "text/html") == true)
        {
            context.Response.Redirect("/Account/Manage/ChangePassword");
        }
        else
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
        }

        return;
    }

    await next();
});

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Frpm.Client._Imports).Assembly);

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();
app.MapRemoteServices(services =>
{
    services.For<Frpm.Abstractions.Services.ICliPackageService>()
        .Operation(nameof(Frpm.Abstractions.Services.ICliPackageService.UploadAsync))
        .DisableAntiforgery();
});
app.MapFrpmApi();
app.MapHealthChecks("/health");

app.Run();
