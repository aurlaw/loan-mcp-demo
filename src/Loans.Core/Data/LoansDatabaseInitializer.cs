using Loans.Core.Data.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Loans.Core.Data;

/// <summary>Applies migrations and seeds demo data. Intended for Development startup.</summary>
public sealed class LoansDatabaseInitializer(LoansDbContext db, LoansSeeder seeder, ILogger<LoansDatabaseInitializer> logger)
{
    public async Task InitializeAsync(bool resetDatabase, CancellationToken cancellationToken = default)
    {
        if (resetDatabase)
        {
            logger.LogWarning("Database:ResetOnStartup is enabled; deleting the database");
            await db.Database.EnsureDeletedAsync(cancellationToken);
        }

        await db.Database.MigrateAsync(cancellationToken);
        await seeder.SeedAsync(cancellationToken);
    }
}
