using Loans.Core.Data;
using Loans.Core.Security;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using System.Security.Claims;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Loans.Web.OAuth;

public static class TokenEndpoint
{
    public static IEndpointRouteBuilder MapLoansTokenEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/connect/token", ExchangeAsync).AllowAnonymous();
        return endpoints;
    }

    private static async Task<IResult> ExchangeAsync(
        HttpContext context,
        UserManager<ApplicationUser> users,
        IOpenIddictAuthorizationManager authorizations,
        IOpenIddictTokenManager tokens,
        IOptions<OAuthOptions> options)
    {
        var request = context.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenIddict server request cannot be retrieved.");

        if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
        {
            return InvalidGrant("The grant type is not supported.");
        }

        var result = await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        if (result.Principal is not { } principal)
        {
            return InvalidGrant("The token is no longer valid.");
        }

        var user = principal.GetClaim(Claims.Subject) is { } subject ? await users.FindByIdAsync(subject) : null;
        if (user is null || await users.IsLockedOutAsync(user))
        {
            return InvalidGrant("The account is no longer available.");
        }

        var roles = await users.GetRolesAsync(user);
        if (!RoleCapabilities.HasMcpAccess(roles))
        {
            await RevokeAsync(principal, authorizations, tokens);
            return InvalidGrant("This account can't be used with AI assistants.");
        }

        var scopes = OAuthScopePolicy.Narrow(principal.GetScopes(), roles);
        if (scopes.IsDefaultOrEmpty)
        {
            return InvalidGrant("The account no longer has access to the granted scopes.");
        }

        // Keep OpenIddict's private claims (authorization ID, presenters); replace user data.
        var identity = new ClaimsIdentity(
            principal.Claims,
            TokenValidationParameters.DefaultAuthenticationType,
            Claims.Name,
            Claims.Role);
        OAuthIdentity.Apply(identity, user, roles, scopes, options.Value.McpResource);

        return Results.SignIn(new ClaimsPrincipal(identity), authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task RevokeAsync(
        ClaimsPrincipal principal,
        IOpenIddictAuthorizationManager authorizations,
        IOpenIddictTokenManager tokens)
    {
        if (principal.GetAuthorizationId() is not { } authorizationId)
        {
            return;
        }

        if (await authorizations.FindByIdAsync(authorizationId) is { } authorization)
        {
            await authorizations.TryRevokeAsync(authorization);
        }

        await tokens.RevokeByAuthorizationIdAsync(authorizationId);
    }

    private static IResult InvalidGrant(string description) => Results.Forbid(
        new AuthenticationProperties(new Dictionary<string, string?>
        {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
        }),
        [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
}
