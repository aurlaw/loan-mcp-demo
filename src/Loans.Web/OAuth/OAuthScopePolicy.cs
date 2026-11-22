using System.Collections.Immutable;
using Loans.Core.Security;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Loans.Web.OAuth;

/// <summary>Scope decisions; role → capability logic stays in <see cref="RoleCapabilities"/>.</summary>
public static class OAuthScopePolicy
{
    public static readonly ImmutableArray<string> IdentityScopes =
        [Scopes.OpenId, Scopes.Profile, Scopes.Email, Scopes.OfflineAccess];

    /// <summary>
    /// Requested ∩ (role capability scopes ∪ identity scopes). No capability scope requested means all the role has.
    /// Always includes offline_access. Empty when no capability scope survives.
    /// </summary>
    public static ImmutableArray<string> Grant(IEnumerable<string> requested, IEnumerable<string> roles)
    {
        var requestedSet = requested.ToHashSet(StringComparer.Ordinal);
        var roleScopes = RoleScopes(roles);

        var requestedCapabilities = CapabilityScopes.All.Where(requestedSet.Contains).ToList();
        var capabilities = requestedCapabilities.Count == 0
            ? roleScopes
            : requestedCapabilities.Where(roleScopes.Contains).ToList();

        if (capabilities.Count == 0)
        {
            return [];
        }

        return
        [
            .. IdentityScopes.Where(scope => scope == Scopes.OfflineAccess || requestedSet.Contains(scope)),
            .. capabilities,
        ];
    }

    /// <summary>Stored scopes limited to what the role allows now (never widens). Empty when no capability survives.</summary>
    public static ImmutableArray<string> Narrow(IEnumerable<string> stored, IEnumerable<string> roles)
    {
        var roleScopes = RoleScopes(roles);
        var kept = stored.Where(scope => IdentityScopes.Contains(scope) || roleScopes.Contains(scope)).ToImmutableArray();
        return kept.Any(roleScopes.Contains) ? kept : [];
    }

    public static IReadOnlyList<Capability> Capabilities(IEnumerable<string> scopes) =>
        scopes.Select(CapabilityScopes.ToCapability).OfType<Capability>().Distinct().ToList();

    private static List<string> RoleScopes(IEnumerable<string> roles) =>
        RoleCapabilities.For(roles).Order().Select(CapabilityScopes.For).ToList();
}
