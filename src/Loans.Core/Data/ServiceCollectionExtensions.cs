using Loans.Core.Data;
using Loans.Core.Data.Queries;
using Loans.Core.Data.Seeding;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class LoansCoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers the DbContext (<c>ConnectionStrings:Loans</c>), Identity core stores, query services and seeding.
    /// A relative SQLite data source is resolved against <paramref name="contentRootPath"/>.
    /// </summary>
    public static IServiceCollection AddLoansCore(this IServiceCollection services, IConfiguration configuration, string contentRootPath)
    {
        var connectionString = configuration.GetConnectionString("Loans")
            ?? throw new InvalidOperationException("Connection string 'ConnectionStrings:Loans' is not configured.");

        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (!string.IsNullOrEmpty(builder.DataSource)
            && builder.Mode != SqliteOpenMode.Memory
            && builder.DataSource != ":memory:"
            && !Path.IsPathRooted(builder.DataSource))
        {
            builder.DataSource = Path.Combine(contentRootPath, builder.DataSource);
        }

        return services.AddLoansCore(builder.ToString(), configuration["Seed:DefaultPassword"]);
    }

    public static IServiceCollection AddLoansCore(this IServiceCollection services, string connectionString, string? seedDefaultPassword)
    {
        services.AddDbContext<LoansDbContext>(options => options.UseSqlite(connectionString));

        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<LoansDbContext>();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(new SeedSettings(seedDefaultPassword));
        services.AddScoped<LoansSeeder>();
        services.AddScoped<LoansDatabaseInitializer>();

        services.AddScoped<ILoanQueries, LoanQueries>();
        services.AddScoped<IApplicationQueries, ApplicationQueries>();
        services.AddScoped<IBorrowerQueries, BorrowerQueries>();

        return services;
    }
}
