using Microsoft.Extensions.Options;

namespace Frpm.Components.Account;

internal sealed class ApplicationUserClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    IOptions<IdentityOptions> optionsAccessor)
    : UserClaimsPrincipalFactory<ApplicationUser>(userManager, optionsAccessor)
{
    public const string MustChangePasswordClaim = "frpm:must_change_password";

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        if (user.MustChangePassword)
        {
            identity.AddClaim(new Claim(MustChangePasswordClaim, bool.TrueString));
        }

        return identity;
    }
}
