using Loans.Core.Domain;
using Loans.Core.Engine;
using Microsoft.EntityFrameworkCore;

namespace Loans.Core.Data.Queries;

internal sealed class DashboardQueries(LoansDbContext db) : IDashboardQueries
{
    public async Task<DashboardSummary> GetSummaryAsync(int? borrowerScope, CancellationToken cancellationToken = default)
    {
        var loans = await db.Loans
            .AsNoTracking()
            .Include(l => l.Payments)
            .Where(l => borrowerScope == null || l.BorrowerId == borrowerScope)
            .ToListAsync(cancellationToken);

        var applicationStatuses = await db.Applications
            .AsNoTracking()
            .Where(a => borrowerScope == null || a.BorrowerId == borrowerScope)
            .Select(a => a.Status)
            .ToListAsync(cancellationToken);

        var loansByStatus = Enum.GetValues<LoanStatus>().ToDictionary(s => s, s => loans.Count(l => l.Status == s));
        var applicationsByStatus = Enum.GetValues<ApplicationStatus>().ToDictionary(s => s, s => applicationStatuses.Count(a => a == s));

        var totalOutstanding = loans
            .Where(l => l.Status != LoanStatus.PaidOff)
            .Sum(BalanceCalculator.CurrentBalanceCents);

        var pastDue = loans.Where(l => l.Status == LoanStatus.PastDue).ToList();
        var borrowerIds = pastDue.Select(l => l.BorrowerId).Distinct().ToList();
        var borrowerNames = await db.Borrowers
            .AsNoTracking()
            .Where(b => borrowerIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => b.LegalName, cancellationToken);

        var pastDueLoans = pastDue
            .Select(l => new PastDueLoan(
                l.Id,
                l.LoanNumber,
                l.BorrowerId,
                borrowerNames[l.BorrowerId],
                BalanceCalculator.CurrentBalanceCents(l),
                l.Payments.Where(p => p.Status == PaymentStatus.Missed).Select(p => (DateOnly?)p.DueDate).Min()))
            .OrderBy(p => p.OldestMissedDueDate ?? DateOnly.MaxValue)
            .ThenBy(p => p.LoanNumber)
            .ToList();

        return new DashboardSummary(loansByStatus, applicationsByStatus, totalOutstanding, pastDueLoans);
    }
}
