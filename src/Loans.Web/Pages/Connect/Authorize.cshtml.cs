using System.Collections.Immutable;
using System.Security.Claims;
using Loans.Core.Data;
using Loans.Core.Security;
using Loans.Web.Infrastructure;
using Loans.Web.OAuth;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Loans.Web.Pages.Connect;

/// <summary>OAuth authorization endpoint (passthrough) and consent page.</summary>
[AllowAnonymous]
public class AuthorizeModel(
    UserManager<ApplicationUser> users,
    IOpenIddictApplicationManager applications,
    IOpenIddictAuthorizationManager authorizations,
    IOptions<OAuthOptions> options) : PageModel
{
    private const string ConsentField = "consent";
    private const string AntiforgeryField = "__RequestVerificationToken";

    public string ClientName { get; private set; } = string.Empty;
    public string? ClientHost { get; private set; }
    public string UserDisplayName { get; private set; } = string.Empty;
    public IReadOnlyList<string> RoleLabels { get; private set; } = [];
    public IReadOnlyList<string> CapabilityLabels { get; private set; } = [];
    public IReadOnlyList<KeyValuePair<string, string>> OAuthParameters { get; private set; } = [];

    public Task<IActionResult> OnGetAsync(CancellationToken cancellationToken) => ProcessAsync(null, cancellationToken);

    public Task<IActionResult> OnPostAsync(CancellationToken cancellationToken) =>
        ProcessAsync(Request.Form[ConsentField].ToString(), cancellationToken);

    private async Task<IActionResult> ProcessAsync(string? consent, CancellationToken cancellationToken)
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenIddict server request cannot be retrieved.");

        // Explicitly the Identity cookie: never any other scheme.
        var cookie = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var user = cookie.Succeeded ? await users.GetUserAsync(cookie.Principal) : null;
        if (user is null)
        {
            return request.HasPromptValue(PromptValues.None)
                ? Refuse(Errors.LoginRequired, "The user is not signed in.")
                : Challenge(
                    new AuthenticationProperties { RedirectUri = Request.PathBase + Request.Path + QueryString.Create(CurrentParameters()) },
                    IdentityConstants.ApplicationScheme);
        }

        var roles = await users.GetRolesAsync(user);
        if (await users.IsLockedOutAsync(user) || !RoleCapabilities.HasMcpAccess(roles))
        {
            return Refuse(Errors.AccessDenied, "This account can't be used with AI assistants.");
        }

        var scopes = OAuthScopePolicy.Grant(request.GetScopes(), roles);
        if (scopes.IsDefaultOrEmpty)
        {
            return Refuse(Errors.AccessDenied, "This account doesn't have access to the requested permissions.");
        }

        var application = await applications.FindByClientIdAsync(request.ClientId!, cancellationToken)
            ?? throw new InvalidOperationException("The client application cannot be found.");

        if (string.Equals(consent, "deny", StringComparison.Ordinal))
        {
            return Refuse(Errors.AccessDenied, "The request was denied.");
        }

        var applicationId = await applications.GetIdAsync(application, cancellationToken);
        object? authorization = null;
        await foreach (var existing in authorizations.FindAsync(user.Id, applicationId, Statuses.Valid, AuthorizationTypes.Permanent, scopes, cancellationToken))
        {
            authorization = existing;
            break;
        }

        var allowed = string.Equals(consent, "allow", StringComparison.Ordinal);
        var remembered = authorization is not null && !request.HasPromptValue(PromptValues.Consent);
        if (!allowed && !remembered)
        {
            if (request.HasPromptValue(PromptValues.None))
            {
                return Refuse(Errors.ConsentRequired, "Interactive consent is required.");
            }

            await PrepareConsentAsync(application, user, roles, scopes, cancellationToken);
            return Page();
        }

        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
        OAuthIdentity.Apply(identity, user, roles, scopes, options.Value.McpResource);

        authorization ??= await authorizations.CreateAsync(identity, user.Id, applicationId!, AuthorizationTypes.Permanent, scopes, cancellationToken);
        identity.SetAuthorizationId(await authorizations.GetIdAsync(authorization, cancellationToken));

        return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private async Task PrepareConsentAsync(
        object application,
        ApplicationUser user,
        IEnumerable<string> roles,
        ImmutableArray<string> scopes,
        CancellationToken cancellationToken)
    {
        ClientName = await applications.GetLocalizedDisplayNameAsync(application, cancellationToken) ?? "This application";

        var properties = await applications.GetPropertiesAsync(application, cancellationToken);
        ClientHost = properties.TryGetValue(OAuthApplicationDescriptors.CimdHostProperty, out var host) ? host.GetString() : null;

        UserDisplayName = user.DisplayName;
        RoleLabels = roles.Select(Formatting.Humanize).ToList();
        CapabilityLabels = RoleCapabilities.Labels(OAuthScopePolicy.Capabilities(scopes));
        OAuthParameters = CurrentParameters().Select(p => new KeyValuePair<string, string>(p.Key, p.Value ?? string.Empty)).ToList();

        // Consent must not be framed (clickjacking) or cached.
        Response.Headers.XFrameOptions = "DENY";
        Response.Headers.ContentSecurityPolicy = "frame-ancestors 'none'";
        Response.Headers.CacheControl = "no-store";
    }

    private List<KeyValuePair<string, string?>> CurrentParameters()
    {
        IEnumerable<KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues>> source =
            Request.HasFormContentType ? Request.Form : Request.Query;

        return source
            .Where(p => p.Key is not (ConsentField or AntiforgeryField))
            .SelectMany(p => p.Value.Select(v => new KeyValuePair<string, string?>(p.Key, v)))
            .ToList();
    }

    private ForbidResult Refuse(string error, string description) => Forbid(
        new AuthenticationProperties(new Dictionary<string, string?>
        {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
        }),
        OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
}
