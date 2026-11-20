using Loans.Core.Domain;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Loans.Core.Data;

/// <summary>Single context for Identity and domain tables (OpenIddict joins in Phase 4).</summary>
public sealed class LoansDbContext(DbContextOptions<LoansDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Borrower> Borrowers => Set<Borrower>();
    public DbSet<Application> Applications => Set<Application>();
    public DbSet<Loan> Loans => Set<Loan>();
    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(LoansDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Readable enum values in the database.
        configurationBuilder.Properties<Enum>().HaveConversion<string>();

        // SQLite cannot compare/order DateTimeOffset; store as a sortable binary (UTC ticks + offset) long.
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
    }
}
