using System.Collections.Concurrent;
using Loans.Core.Common;
using Loans.Web.OAuth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Loans.Web.Tests.OAuth;

public sealed class FakeClientMetadataFetcher : IClientMetadataFetcher
{
    private readonly ConcurrentDictionary<string, FetchedClientMetadata> _documents = new(StringComparer.Ordinal);

    public void Set(string url, string json, TimeSpan? maxAge = null) => _documents[url] = new FetchedClientMetadata(json, maxAge);

    public Task<Result<FetchedClientMetadata>> FetchAsync(Uri url, CancellationToken cancellationToken = default) =>
        Task.FromResult(_documents.TryGetValue(url.OriginalString, out var document)
            ? Result<FetchedClientMetadata>.Success(document)
            : Result<FetchedClientMetadata>.Failure(new Error(HttpClientMetadataFetcher.FetchFailedCode, "Not found.")));
}

public sealed class AdjustableTimeProvider : TimeProvider
{
    private TimeSpan _offset;

    public void Advance(TimeSpan by) => _offset += by;

    public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + _offset;
}

/// <summary>Hosts the app as an authorization server on the test host with a fake CIMD fetcher and isolated storage.</summary>
public sealed class OAuthWebFactory : WebApplicationFactory<Program>
{
    public const string Password = LoansWebFactory.Password;
    public const string Issuer = "http://localhost/";
    public const string Resource = "http://localhost/mcp";

    private readonly string _dbPath;
    private readonly string _keyPath;
    private readonly bool _ownsStorage;
    private readonly string _environment;
    private readonly IReadOnlyDictionary<string, string> _settings;

    public OAuthWebFactory() : this(new Dictionary<string, string>())
    {
    }

    public OAuthWebFactory(IReadOnlyDictionary<string, string> settings, string environment = "Development", OAuthWebFactory? shareStorageWith = null)
    {
        _settings = settings;
        _environment = environment;
        _ownsStorage = shareStorageWith is null;
        _dbPath = shareStorageWith?._dbPath ?? Path.Combine(Path.GetTempPath(), $"loans-oauth-test-{Guid.NewGuid():N}.db");
        _keyPath = shareStorageWith?._keyPath ?? Path.Combine(Path.GetTempPath(), $"loans-oauth-keys-{Guid.NewGuid():N}");
    }

    public FakeClientMetadataFetcher Fetcher { get; } = new();
    public AdjustableTimeProvider Clock { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        builder.UseSetting("ConnectionStrings:Loans", $"Data Source={_dbPath}");
        builder.UseSetting("Seed:DefaultPassword", Password);
        builder.UseSetting("Database:ResetOnStartup", "false");
        builder.UseSetting("OAuth:Issuer", Issuer);
        builder.UseSetting("OAuth:McpResource", Resource);
        builder.UseSetting("OAuth:KeyStoragePath", _keyPath);
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IClientMetadataFetcher>();
            services.AddSingleton<IClientMetadataFetcher>(Fetcher);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    public HttpClient CreateBrowser() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (!_ownsStorage)
        {
            return;
        }

        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            File.Delete(path);
        }

        if (Directory.Exists(_keyPath))
        {
            Directory.Delete(_keyPath, recursive: true);
        }
    }
}
