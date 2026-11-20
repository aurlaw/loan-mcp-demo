using System.Security.Claims;
using Loans.Core.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Loans.Web.Infrastructure;

/// <summary>Adds the user's display name to the cookie principal (shown in the header).</summary>
public sealed class DisplayNameClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(LoansClaimTypes.DisplayName, user.DisplayName));
        return identity;
    }
}
