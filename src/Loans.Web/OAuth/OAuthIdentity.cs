using System.Collections.Immutable;
using System.Security.Claims;
using Loans.Core.Data;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Loans.Web.OAuth;

/// <summary>Applies user claims, scopes, resource and destinations to an OpenIddict identity.</summary>
public static class OAuthIdentity
{
    public static ClaimsIdentity Apply(
        ClaimsIdentity identity,
        ApplicationUser user,
        IEnumerable<string> roles,
        ImmutableArray<string> scopes,
        string resource)
    {
        identity
            .SetClaim(Claims.Subject, user.Id)
            .SetClaim(Claims.Name, user.DisplayName)
            .SetClaim(Claims.Email, user.Email)
            .SetClaims(Claims.Role, [.. roles]);

        identity.SetScopes(scopes);
        identity.SetResources(resource);
        identity.SetDestinations(claim => Destinations(claim, identity));
        return identity;
    }

    private static IEnumerable<string> Destinations(Claim claim, ClaimsIdentity identity)
    {
        switch (claim.Type)
        {
            case Claims.Subject:
                yield return OpenIddictConstants.Destinations.AccessToken;
                yield return OpenIddictConstants.Destinations.IdentityToken;
                break;

            case Claims.Name or Claims.Role:
                yield return OpenIddictConstants.Destinations.AccessToken;
                if (identity.HasScope(Scopes.Profile))
                {
                    yield return OpenIddictConstants.Destinations.IdentityToken;
                }

                break;

            case Claims.Email when identity.HasScope(Scopes.Email):
                yield return OpenIddictConstants.Destinations.IdentityToken;
                break;
        }
    }
}
