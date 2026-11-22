using Microsoft.Extensions.Options;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Loans.Web.OAuth;

/// <summary>Advertises CIMD, public-client auth and RFC 9207 in the discovery document.</summary>
public sealed class DiscoveryMetadataHandler(IOptions<OAuthOptions> options) : IOpenIddictServerHandler<HandleConfigurationRequestContext>
{
    public static OpenIddictServerHandlerDescriptor Descriptor { get; } =
        OpenIddictServerHandlerDescriptor.CreateBuilder<HandleConfigurationRequestContext>()
            .UseSingletonHandler<DiscoveryMetadataHandler>()
            .SetOrder(OpenIddictServerHandlers.Discovery.AttachClientAuthenticationMethods.Descriptor.Order + 1_000)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    public ValueTask HandleAsync(HandleConfigurationRequestContext context)
    {
        // OpenIddict pins "issuer" but derives endpoints from the request host; pin them too
        // so the tunnel or a spoofed Host header can never change what clients are told.
        var issuer = new Uri(EnsureTrailingSlash(options.Value.Issuer), UriKind.Absolute);
        context.AuthorizationEndpoint = new Uri(issuer, "connect/authorize");
        context.TokenEndpoint = new Uri(issuer, "connect/token");
        context.JsonWebKeySetEndpoint = new Uri(issuer, ".well-known/jwks");

        // Exactly what this server supports (no private_key_jwt).
        context.TokenEndpointAuthenticationMethods.Clear();
        context.TokenEndpointAuthenticationMethods.Add(ClientAuthenticationMethods.None);
        context.TokenEndpointAuthenticationMethods.Add(ClientAuthenticationMethods.ClientSecretBasic);
        context.TokenEndpointAuthenticationMethods.Add(ClientAuthenticationMethods.ClientSecretPost);

        context.Metadata[Metadata.AuthorizationResponseIssParameterSupported] = true;

        if (options.Value.Cimd.Enabled)
        {
            context.Metadata["client_id_metadata_document_supported"] = true;
        }

        return ValueTask.CompletedTask;
    }

    private static string EnsureTrailingSlash(string value) => value.EndsWith('/') ? value : value + "/";
}
