using System.Text.Json;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;
using Loans.Core.Security;

namespace Loans.Web.OAuth;

/// <summary>Permissions and requirements shared by CIMD and pre-registered clients.</summary>
public static class OAuthApplicationDescriptors
{
    public const string CimdHostProperty = "cimd_host";
    public const string CimdFetchedAtProperty = "cimd_fetched_at";

    public static void Apply(
        OpenIddictApplicationDescriptor descriptor,
        string clientId,
        string displayName,
        string clientType,
        IEnumerable<string> redirectUris,
        string resource)
    {
        descriptor.ClientId = clientId;
        descriptor.DisplayName = displayName;
        descriptor.ClientType = clientType;
        descriptor.ConsentType = ConsentTypes.Explicit;

        descriptor.RedirectUris.Clear();
        foreach (var uri in redirectUris)
        {
            descriptor.RedirectUris.Add(new Uri(uri, UriKind.Absolute));
        }

        descriptor.Requirements.Clear();
        descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);

        descriptor.Permissions.Clear();
        descriptor.Permissions.UnionWith(
        [
            Permissions.Endpoints.Authorization,
            Permissions.Endpoints.Token,
            Permissions.GrantTypes.AuthorizationCode,
            Permissions.GrantTypes.RefreshToken,
            Permissions.ResponseTypes.Code,
            Permissions.Scopes.Profile,
            Permissions.Scopes.Email,
            Permissions.Prefixes.Resource + resource,
        ]);
        descriptor.Permissions.UnionWith(CapabilityScopes.All.Select(scope => Permissions.Prefixes.Scope + scope));
    }

    public static void ApplyCimd(OpenIddictApplicationDescriptor descriptor, CimdClient client, string resource)
    {
        var metadata = client.Metadata;
        Apply(descriptor, metadata.ClientId, metadata.DisplayName, ClientTypes.Public, metadata.RedirectUris, resource);
        descriptor.Properties[CimdHostProperty] = JsonSerializer.SerializeToElement(metadata.Host);
        descriptor.Properties[CimdFetchedAtProperty] = JsonSerializer.SerializeToElement(client.FetchedAt.ToUnixTimeMilliseconds());
    }
}
