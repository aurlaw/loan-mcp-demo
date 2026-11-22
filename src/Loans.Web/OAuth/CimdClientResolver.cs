using System.Collections.Concurrent;
using Loans.Core.Common;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Loans.Web.OAuth;

/// <summary>A resolved CIMD client; <see cref="FetchedAt"/> identifies the document version.</summary>
public sealed record CimdClient(ClientMetadata Metadata, DateTimeOffset FetchedAt);

public interface ICimdClientResolver
{
    Task<Result<CimdClient>> ResolveAsync(string clientId, CancellationToken cancellationToken = default);
}

/// <summary>Cache + fetch + validate. Honors max-age within configured bounds; negative-caches failures.</summary>
public sealed class CimdClientResolver(
    IClientMetadataFetcher fetcher,
    IOptions<OAuthOptions> options,
    TimeProvider timeProvider,
    ILogger<CimdClientResolver> logger) : ICimdClientResolver, IDisposable
{
    private const int MaxCachedClients = 1024;
    private static readonly TimeSpan NegativeCacheDuration = TimeSpan.FromMinutes(1);

    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = MaxCachedClients });
    private readonly ConcurrentDictionary<string, Lazy<Task<Result<CimdClient>>>> _inFlight = new(StringComparer.Ordinal);

    public async Task<Result<CimdClient>> ResolveAsync(string clientId, CancellationToken cancellationToken = default)
    {
        var url = ClientMetadataValidator.ValidateClientId(clientId);
        if (url.IsFailure)
        {
            return Result<CimdClient>.Failure(url.Error);
        }

        if (_cache.TryGetValue<CacheEntry>(clientId, out var cached) && cached!.ExpiresAt > timeProvider.GetUtcNow())
        {
            return cached.Result;
        }

        // One fetch per URL at a time; not tied to any single caller's cancellation.
        var load = _inFlight.GetOrAdd(clientId, id => new Lazy<Task<Result<CimdClient>>>(() => LoadAsync(id, url.Value)));
        return await load.Value.WaitAsync(cancellationToken);
    }

    public void Dispose() => _cache.Dispose();

    private async Task<Result<CimdClient>> LoadAsync(string clientId, Uri url)
    {
        try
        {
            var now = timeProvider.GetUtcNow();
            var fetched = await fetcher.FetchAsync(url);
            if (fetched.IsFailure)
            {
                return Store(clientId, Result<CimdClient>.Failure(fetched.Error), NegativeCacheDuration, now);
            }

            var metadata = ClientMetadataValidator.ValidateDocument(clientId, fetched.Value.Json);
            if (metadata.IsFailure)
            {
                return Store(clientId, Result<CimdClient>.Failure(metadata.Error), NegativeCacheDuration, now);
            }

            return Store(clientId, Result<CimdClient>.Success(new CimdClient(metadata.Value, now)), ClampTtl(fetched.Value.MaxAge), now);
        }
        finally
        {
            _inFlight.TryRemove(clientId, out _);
        }
    }

    private TimeSpan ClampTtl(TimeSpan? maxAge)
    {
        var min = TimeSpan.FromMinutes(options.Value.Cimd.MinCacheMinutes);
        var max = TimeSpan.FromHours(options.Value.Cimd.MaxCacheHours);
        var value = maxAge ?? min;
        return value < min ? min : value > max ? max : value;
    }

    private Result<CimdClient> Store(string clientId, Result<CimdClient> result, TimeSpan ttl, DateTimeOffset now)
    {
        if (result.IsFailure)
        {
            logger.LogWarning("CIMD resolution failed for {ClientId}: {Code} {Message}", clientId, result.Error.Code, result.Error.Message);
        }

        _cache.Set(clientId, new CacheEntry(result, now + ttl), new MemoryCacheEntryOptions
        {
            Size = 1,
            AbsoluteExpirationRelativeToNow = ttl,
        });
        return result;
    }

    private sealed record CacheEntry(Result<CimdClient> Result, DateTimeOffset ExpiresAt);
}
