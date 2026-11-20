using Loans.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Loans.Core.Data.Queries;

internal sealed class BorrowerQueries(LoansDbContext db) : IBorrowerQueries
{
    public Task<BorrowerSummary?> GetByLinkedUserIdAsync(string userId, CancellationToken cancellationToken = default) =>
        db.Borrowers
            .AsNoTracking()
            .Where(b => b.LinkedUserId == userId)
            .Select(b => ToSummary(b))
            .FirstOrDefaultAsync(cancellationToken);

    internal static BorrowerSummary ToSummary(Borrower b) =>
        new(b.Id, b.LegalName, b.EntityType, b.Industry, b.YearsInBusiness, b.State);
}
