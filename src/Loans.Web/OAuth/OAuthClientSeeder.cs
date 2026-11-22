using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Loans.Web.OAuth;

/// <summary>Idempotently seeds pre-registered fallback clients from <c>OAuth:Clients</c>.</summary>
public sealed class OAuthClientSeeder(
    IOpenIddictApplicationManager applications,
    IOptions<OAuthOptions> options,
    IHostEnvironment environment,
    ILogger<OAuthClientSeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        foreach (var (name, client) in options.Value.Clients)
        {
            if (string.IsNullOrWhiteSpace(client.ClientId))
            {
                // e.g. a leftover secret in user secrets with no client definition.
                logger.LogWarning("Skipping OAuth client entry {Name}: no ClientId configured", name);
                continue;
            }

            if (client.DevelopmentOnly && !environment.IsDevelopment())
            {
                continue;
            }

            var clientType = ParseType(name, client);
            if (ClientMetadataValidator.IsUrlShaped(client.ClientId))
            {
                throw new InvalidOperationException($"OAuth client '{name}': ClientId must not be an https URL (reserved for CIMD).");
            }

            if (client.RedirectUris.Count == 0)
            {
                throw new InvalidOperationException($"OAuth client '{name}': at least one redirect URI is required.");
            }

            if (clientType == ClientTypes.Confidential && string.IsNullOrWhiteSpace(client.Secret))
            {
                throw new InvalidOperationException(
                    $"OAuth client '{name}' is Confidential but OAuth:Clients:{name}:Secret is not set. " +
                    $"Set it with: dotnet user-secrets set \"OAuth:Clients:{name}:Secret\" \"<secret>\" --project src/Loans.Web");
            }

            var descriptor = new OpenIddictApplicationDescriptor();
            var existing = await applications.FindByClientIdAsync(client.ClientId, cancellationToken);
            if (existing is not null)
            {
                await applications.PopulateAsync(descriptor, existing, cancellationToken);
            }

            OAuthApplicationDescriptors.Apply(
                descriptor,
                client.ClientId,
                client.DisplayName ?? name,
                clientType,
                client.RedirectUris,
                options.Value.McpResource);
            descriptor.ClientSecret = clientType == ClientTypes.Confidential ? client.Secret : null;

            if (existing is null)
            {
                await applications.CreateAsync(descriptor, cancellationToken);
            }
            else
            {
                await applications.UpdateAsync(existing, descriptor, cancellationToken);
            }
        }
    }

    private static string ParseType(string name, OAuthClientOptions client) => client.Type switch
    {
        _ when client.Type.Equals("Confidential", StringComparison.OrdinalIgnoreCase) => ClientTypes.Confidential,
        _ when client.Type.Equals("Public", StringComparison.OrdinalIgnoreCase) => ClientTypes.Public,
        _ => throw new InvalidOperationException($"OAuth client '{name}': Type must be Confidential or Public."),
    };
}
