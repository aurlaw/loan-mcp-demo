using Loans.Core.Engine;
using Microsoft.EntityFrameworkCore;

namespace Loans.Core.Data.Queries;

internal sealed class LoanQueries(LoansDbContext db) : ILoanQueries
{
    public async Task<PagedResult<LoanSummary>> SearchAsync(LoanSearch criteria, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        var (page, pageSize) = Paging.Normalize(criteria.Page, criteria.PageSize);

        var query = db.Loans.AsNoTracking();

        if (criteria.BorrowerId is { } borrowerId)
        {
            query = query.Where(l => l.BorrowerId == borrowerId);
        }

        if (criteria.Status is { } status)
        {
            query = query.Where(l => l.Status == status);
        }

        if (criteria.Product is { } product)
        {
            query = query.Where(l => l.Product == product);
        }

        if (!string.IsNullOrWhiteSpace(criteria.Text))
        {
            var pattern = Paging.ContainsPattern(criteria.Text);
            query = query.Where(l =>
                EF.Functions.Like(l.LoanNumber, pattern, Paging.LikeEscape)
                || db.Borrowers.Any(b => b.Id == l.BorrowerId && EF.Functions.Like(b.LegalName, pattern, Paging.LikeEscape)));
        }

        var total = await query.CountAsync(cancellationToken);

        var loans = await query
            .OrderBy(l => l.LoanNumber)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(l => l.Payments)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var borrowerIds = loans.Select(l => l.BorrowerId).Distinct().ToList();
        var borrowerNames = await db.Borrowers
            .AsNoTracking()
            .Where(b => borrowerIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => b.LegalName, cancellationToken);

        var items = loans
            .Select(l => new LoanSummary(
                l.Id,
                l.LoanNumber,
                l.BorrowerId,
                borrowerNames[l.BorrowerId],
                l.Product,
                l.OriginalPrincipalCents,
                BalanceCalculator.CurrentBalanceCents(l),
                l.RateBps,
                l.MaturityDate,
                l.Status))
            .ToList();

        return new PagedResult<LoanSummary>(items, total, page, pageSize);
    }

    public async Task<LoanDetail?> GetDetailAsync(int loanId, int? borrowerScope, CancellationToken cancellationToken = default)
    {
        var loan = await db.Loans
            .AsNoTracking()
            .Include(l => l.Payments)
            .Where(l => l.Id == loanId && (borrowerScope == null || l.BorrowerId == borrowerScope))
            .FirstOrDefaultAsync(cancellationToken);

        if (loan is null)
        {
            return null;
        }

        var borrower = await db.Borrowers.AsNoTracking().FirstAsync(b => b.Id == loan.BorrowerId, cancellationToken);

        var payments = loan.Payments
            .OrderByDescending(p => p.DueDate)
            .Select(p => new PaymentDto(p.Id, p.DueDate, p.PaidDate, p.AmountCents, p.PrincipalCents, p.InterestCents, p.Status))
            .ToList();

        return new LoanDetail(
            loan.Id,
            loan.LoanNumber,
            BorrowerQueries.ToSummary(borrower),
            loan.SourceApplicationId,
            loan.Product,
            loan.OriginalPrincipalCents,
            loan.RateBps,
            loan.TermMonths,
            loan.AmortizationMonths,
            loan.OriginationDate,
            loan.MaturityDate,
            loan.Status,
            BalanceCalculator.CurrentBalanceCents(loan),
            BalanceCalculator.PaidThroughDate(loan),
            payments);
    }
}
