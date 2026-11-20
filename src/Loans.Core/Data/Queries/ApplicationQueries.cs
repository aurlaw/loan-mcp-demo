using Microsoft.EntityFrameworkCore;

namespace Loans.Core.Data.Queries;

internal sealed class ApplicationQueries(LoansDbContext db) : IApplicationQueries
{
    public async Task<PagedResult<ApplicationSummary>> SearchAsync(ApplicationSearch criteria, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        var (page, pageSize) = Paging.Normalize(criteria.Page, criteria.PageSize);

        var query =
            from a in db.Applications.AsNoTracking()
            join b in db.Borrowers.AsNoTracking() on a.BorrowerId equals b.Id
            select new { Application = a, BorrowerName = b.LegalName };

        if (criteria.BorrowerId is { } borrowerId)
        {
            query = query.Where(x => x.Application.BorrowerId == borrowerId);
        }

        if (criteria.Status is { } status)
        {
            query = query.Where(x => x.Application.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(criteria.Text))
        {
            var pattern = Paging.ContainsPattern(criteria.Text);
            query = query.Where(x =>
                EF.Functions.Like(x.BorrowerName, pattern, Paging.LikeEscape)
                || EF.Functions.Like(x.Application.Purpose, pattern, Paging.LikeEscape));
        }

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(x => x.Application.SubmittedOn)
            .ThenBy(x => x.Application.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new ApplicationSummary(
                x.Application.Id,
                x.Application.BorrowerId,
                x.BorrowerName,
                x.Application.Product,
                x.Application.RequestedAmountCents,
                x.Application.Purpose,
                x.Application.Status,
                x.Application.SubmittedOn,
                x.Application.UnderwritingResult == null ? null : x.Application.UnderwritingResult.Recommendation))
            .ToListAsync(cancellationToken);

        return new PagedResult<ApplicationSummary>(items, total, page, pageSize);
    }

    public async Task<ApplicationDetail?> GetDetailAsync(int applicationId, int? borrowerScope, CancellationToken cancellationToken = default)
    {
        var application = await db.Applications
            .AsNoTracking()
            .Where(a => a.Id == applicationId && (borrowerScope == null || a.BorrowerId == borrowerScope))
            .FirstOrDefaultAsync(cancellationToken);

        if (application is null)
        {
            return null;
        }

        var borrower = await db.Borrowers.AsNoTracking().FirstAsync(b => b.Id == application.BorrowerId, cancellationToken);

        var userIds = new[]
        {
            application.AssignedOfficerUserId,
            application.UnderwritingResult?.RunByUserId,
            application.Decision?.DecidedByUserId,
        }.OfType<string>().Distinct().ToList();

        var names = await db.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);

        var result = application.UnderwritingResult;
        var decision = application.Decision;

        return new ApplicationDetail(
            application.Id,
            BorrowerQueries.ToSummary(borrower),
            application.AssignedOfficerUserId,
            names.GetValueOrDefault(application.AssignedOfficerUserId),
            application.Product,
            application.RequestedAmountCents,
            application.RequestedRateBps,
            application.TermMonths,
            application.AmortizationMonths,
            application.Purpose,
            application.Status,
            application.SubmittedOn,
            new FinancialsDto(
                application.Financials.AnnualRevenueCents,
                application.Financials.NetOperatingIncomeCents,
                application.Financials.ExistingAnnualDebtServiceCents),
            new CollateralDto(application.Collateral.Type, application.Collateral.AppraisedValueCents),
            result is null
                ? null
                : new UnderwritingResultDto(
                    result.DscrX100,
                    result.LtvBps,
                    result.RiskRating,
                    result.Recommendation,
                    [.. result.Exceptions],
                    result.ProposedMonthlyPaymentCents,
                    result.RunAt,
                    result.RunByUserId,
                    names.GetValueOrDefault(result.RunByUserId)),
            decision is null
                ? null
                : new DecisionDto(
                    decision.Outcome,
                    decision.ApprovedRateBps,
                    decision.Conditions,
                    decision.DecidedByUserId,
                    names.GetValueOrDefault(decision.DecidedByUserId),
                    decision.DecidedAt));
    }
}
