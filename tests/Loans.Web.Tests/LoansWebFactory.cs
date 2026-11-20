using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace Loans.Web.Tests;

/// <summary>
/// Hosts Loans.Web in Development against an isolated temp SQLite file; the app's own startup
/// initializer migrates and seeds it with <see cref="Password"/>.
/// </summary>
public sealed class LoansWebFactory : WebApplicationFactory<Program>
{
    public const string Password = "Web-Test-Pa55!";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"loans-web-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Loans", $"Data Source={_dbPath}");
        builder.UseSetting("Seed:DefaultPassword", Password);
        builder.UseSetting("Database:ResetOnStartup", "false");
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            File.Delete(path);
        }
    }
}
