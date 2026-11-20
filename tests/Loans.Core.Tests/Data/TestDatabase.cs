using Loans.Core.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Loans.Core.Tests.Data;

/// <summary>
/// Isolated in-memory SQLite database (shared cache, kept alive by an open connection) wired through
/// <c>AddLoansCore</c>, migrated and seeded with a fixed clock.
/// </summary>
public sealed class TestDatabase : IAsyncDisposable
{
    public static readonly DateTimeOffset Now = new(2026, 11, 20, 15, 0, 0, TimeSpan.Zero);
    public static readonly DateOnly Anchor = new(2026, 11, 1);
    public const string Password = "Test-Seed-Pa55!";

    private readonly string _connectionString;
    private readonly SqliteConnection? _keepAlive;
    private readonly string? _filePath;
    private readonly ServiceProvider _provider;

    private TestDatabase(string? seedPassword, string? filePath = null)
    {
        if (filePath is null)
        {
            _connectionString = $"Data Source=loans-test-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            _keepAlive = new SqliteConnection(_connectionString);
            _keepAlive.Open();
        }
        else
        {
            _filePath = filePath;
            _connectionString = $"Data Source={filePath}";
        }

        _provider = BuildProvider(seedPassword);
    }

    /// <summary>File-backed database; needed where EnsureDeleted must actually drop the database (reset).</summary>
    public static async Task<TestDatabase> CreateSeededFileAsync()
    {
        var db = new TestDatabase(Password, Path.Combine(Path.GetTempPath(), $"loans-test-{Guid.NewGuid():N}.db"));
        await db.InitializeAsync();
        return db;
    }

    /// <summary>Builds another provider over the same in-memory database (e.g. with different seed settings).</summary>
    public ServiceProvider BuildProvider(string? seedPassword)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        services.AddLoansCore(_connectionString, seedPassword);
        return services.BuildServiceProvider(validateScopes: true);
    }

    public static async Task<TestDatabase> CreateSeededAsync()
    {
        var db = new TestDatabase(Password);
        await db.InitializeAsync();
        return db;
    }

    public static TestDatabase CreateUninitialized(string? seedPassword) => new(seedPassword);

    public async Task InitializeAsync(bool reset = false)
    {
        await using var scope = CreateScope();
        await scope.ServiceProvider.GetRequiredService<LoansDatabaseInitializer>().InitializeAsync(reset);
    }

    public AsyncServiceScope CreateScope() => _provider.CreateAsyncScope();

    public async Task<T> WithScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = CreateScope();
        return await action(scope.ServiceProvider);
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();

        if (_keepAlive is not null)
        {
            await _keepAlive.DisposeAsync();
        }

        if (_filePath is not null)
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { _filePath, _filePath + "-wal", _filePath + "-shm" })
            {
                File.Delete(path);
            }
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

/// <summary>Shared seeded database for read-only tests.</summary>
public sealed class SeededDatabaseFixture : IAsyncLifetime
{
    public TestDatabase Database { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Database = await TestDatabase.CreateSeededAsync();

    public ValueTask DisposeAsync() => Database.DisposeAsync();
}
