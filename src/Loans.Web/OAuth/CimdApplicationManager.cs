using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Core;
using OpenIddict.EntityFrameworkCore.Models;

namespace Loans.Web.OAuth;

/// <summary>Serializes creating/updating CIMD applications so concurrent first requests can't race.</summary>
public sealed class CimdMaterializationGate : IDisposable
{
    public SemaphoreSlim Semaphore { get; } = new(1, 1);

    public void Dispose() => Semaphore.Dispose();
}

/// <summary>
/// CIMD hook point: URL-shaped client IDs are resolved from their metadata document and materialized as a
/// public application, so OpenIddict's standard validation then applies at every endpoint.
/// </summary>
public sealed class CimdApplicationManager(
    IOpenIddictApplicationCache<OpenIddictEntityFrameworkCoreApplication> cache,
    ILogger<OpenIddictApplicationManager<OpenIddictEntityFrameworkCoreApplication>> logger,
    IOptionsMonitor<OpenIddictCoreOptions> coreOptions,
    IOpenIddictApplicationStore<OpenIddictEntityFrameworkCoreApplication> store,
    ICimdClientResolver resolver,
    CimdMaterializationGate gate,
    IOptions<OAuthOptions> oauthOptions)
    : OpenIddictApplicationManager<OpenIddictEntityFrameworkCoreApplication>(cache, logger, coreOptions, store)
{
    public override async ValueTask<OpenIddictEntityFrameworkCoreApplication?> FindByClientIdAsync(
        string identifier,
        CancellationToken cancellationToken = default)
    {
        if (!ClientMetadataValidator.IsUrlShaped(identifier))
        {
            return await base.FindByClientIdAsync(identifier, cancellationToken);
        }

        if (!oauthOptions.Value.Cimd.Enabled)
        {
            return null;
        }

        var resolved = await resolver.ResolveAsync(identifier, cancellationToken);
        if (resolved.IsFailure)
        {
            return null;
        }

        await gate.Semaphore.WaitAsync(cancellationToken);
        try
        {
            var application = await base.FindByClientIdAsync(identifier, cancellationToken);
            var descriptor = new OpenIddictApplicationDescriptor();

            if (application is null)
            {
                OAuthApplicationDescriptors.ApplyCimd(descriptor, resolved.Value, oauthOptions.Value.McpResource);
                return (OpenIddictEntityFrameworkCoreApplication)await CreateAsync(descriptor, cancellationToken);
            }

            var properties = await GetPropertiesAsync(application, cancellationToken);
            var isCurrent = properties.TryGetValue(OAuthApplicationDescriptors.CimdFetchedAtProperty, out var fetchedAt)
                && fetchedAt.GetInt64() == resolved.Value.FetchedAt.ToUnixTimeMilliseconds();

            if (!isCurrent)
            {
                await PopulateAsync(descriptor, application, cancellationToken);
                OAuthApplicationDescriptors.ApplyCimd(descriptor, resolved.Value, oauthOptions.Value.McpResource);
                await UpdateAsync(application, descriptor, cancellationToken);
            }

            return application;
        }
        finally
        {
            gate.Semaphore.Release();
        }
    }

    /// <summary>RFC 8252 §7.3: for CIMD clients, a registered loopback redirect matches on any port.</summary>
    public override async ValueTask<bool> ValidateRedirectUriAsync(
        OpenIddictEntityFrameworkCoreApplication application,
        string uri,
        CancellationToken cancellationToken = default)
    {
        if (await base.ValidateRedirectUriAsync(application, uri, cancellationToken))
        {
            return true;
        }

        var clientId = await GetClientIdAsync(application, cancellationToken);
        if (!ClientMetadataValidator.IsUrlShaped(clientId)
            || !Uri.TryCreate(uri, UriKind.Absolute, out var candidate)
            || candidate.Scheme != Uri.UriSchemeHttp
            || !ClientMetadataValidator.IsLoopbackHost(candidate.Host)
            || !string.IsNullOrEmpty(candidate.UserInfo)
            || !string.IsNullOrEmpty(candidate.Fragment))
        {
            return false;
        }

        foreach (var registered in await GetRedirectUrisAsync(application, cancellationToken))
        {
            if (Uri.TryCreate(registered, UriKind.Absolute, out var allowed)
                && allowed.Scheme == candidate.Scheme
                && string.Equals(allowed.Host, candidate.Host, StringComparison.OrdinalIgnoreCase)
                && string.Equals(allowed.PathAndQuery, candidate.PathAndQuery, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
