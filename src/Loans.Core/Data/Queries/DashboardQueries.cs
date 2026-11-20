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

    public async Task<OfficerPipeline> GetOfficerPipelineAsync(string officerUserId, CancellationToken cancellationToken = default)
    {
        var applications = await (
                from a in db.Applications.AsNoTracking()
                join b in db.Borrowers.AsNoTracking() on a.BorrowerId equals b.Id
                where a.AssignedOfficerUserId == officerUserId
                orderby a.SubmittedOn descending, a.Id
                select new ApplicationSummary(
                    a.Id,
                    a.BorrowerId,
                    b.LegalName,
                    a.Product,
                    a.RequestedAmountCents,
                    a.Purpose,
                    a.Status,
                    a.SubmittedOn,
                    a.UnderwritingResult == null ? null : a.UnderwritingResult.Recommendation))
            .ToListAsync(cancellationToken);

        var counts = Enum.GetValues<ApplicationStatus>().ToDictionary(s => s, s => applications.Count(a => a.Status == s));
        return new OfficerPipeline(counts, applications);
    }

    public async Task<UnderwritingQueue> GetUnderwritingQueueAsync(CancellationToken cancellationToken = default)
    {
        var awaiting = await (
                from a in db.Applications.AsNoTracking()
                join b in db.Borrowers.AsNoTracking() on a.BorrowerId equals b.Id
                where a.Status == ApplicationStatus.InUnderwriting && a.UnderwritingResult != null && a.Decision == null
                orderby a.UnderwritingResult!.RunAt, a.Id
                select new AwaitingDecisionItem(
                    a.Id,
                    b.LegalName,
                    a.RequestedAmountCents,
                    a.UnderwritingResult!.DscrX100,
                    a.UnderwritingResult.LtvBps,
                    a.UnderwritingResult.RiskRating,
                    a.UnderwritingResult.Recommendation,
                    a.UnderwritingResult.RunAt))
            .ToListAsync(cancellationToken);

        var notYetUnderwritten = await (
                from a in db.Applications.AsNoTracking()
                join b in db.Borrowers.AsNoTracking() on a.BorrowerId equals b.Id
                where a.Status == ApplicationStatus.Submitted
                orderby a.SubmittedOn, a.Id
                select new NotYetUnderwrittenItem(a.Id, b.LegalName, a.RequestedAmountCents, a.Product, a.SubmittedOn))
            .ToListAsync(cancellationToken);

        var decided = await (
                from a in db.Applications.AsNoTracking()
                join b in db.Borrowers.AsNoTracking() on a.BorrowerId equals b.Id
                join u in db.Users.AsNoTracking() on a.Decision!.DecidedByUserId equals u.Id into deciders
                from u in deciders.DefaultIfEmpty()
                where a.Decision != null
                orderby a.Decision!.DecidedAt descending, a.Id
                select new RecentDecisionItem(
                    a.Id,
                    b.LegalName,
                    a.Decision!.Outcome,
                    a.Decision.ApprovedRateBps,
                    u == null ? null : u.DisplayName,
                    a.Decision.DecidedAt))
            .Take(5)
            .ToListAsync(cancellationToken);

        return new UnderwritingQueue(awaiting, notYetUnderwritten, decided);
    }
}
