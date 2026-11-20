using System.Security.Claims;
using Loans.Core.Security;

namespace Loans.Web.Infrastructure;

public static class PrincipalExtensions
{
    public static IReadOnlyList<string> Roles(this ClaimsPrincipal user) =>
        user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

    /// <summary>Capabilities from the user's roles via <see cref="RoleCapabilities"/> (no role logic in Web).</summary>
    public static IReadOnlySet<Capability> Capabilities(this ClaimsPrincipal user) =>
        RoleCapabilities.For(user.Roles());
}
