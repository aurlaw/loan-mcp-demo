using Loans.Core.Common;

namespace Loans.Core.Data.Queries;

public static class QueryErrors
{
    public const string LoanNotFound = "loan.not_found";
}

public interface ILoanQueries
{
    Task<PagedResult<LoanSummary>> SearchAsync(LoanSearch criteria, CancellationToken cancellationToken = default);

    /// <summary>Returns null when the loan does not exist or is outside <paramref name="borrowerScope"/>.</summary>
    Task<LoanDetail?> GetDetailAsync(int loanId, int? borrowerScope, CancellationToken cancellationToken = default);

    /// <summary>Full amortization schedule with paid installments marked; null when not found or out of scope.</summary>
    Task<LoanSchedule?> GetScheduleAsync(int loanId, int? borrowerScope, CancellationToken cancellationToken = default);

    /// <summary>
    /// Payoff quote via the engine. Fails with <see cref="QueryErrors.LoanNotFound"/> when not found or out of scope,
    /// otherwise propagates engine error codes (e.g. <c>payoff.date_before_paid_through</c>).
    /// </summary>
    Task<Result<PayoffQuoteDto>> GetPayoffQuoteAsync(int loanId, DateOnly payoffDate, int? borrowerScope, CancellationToken cancellationToken = default);
}

public interface IApplicationQueries
{
    Task<PagedResult<ApplicationSummary>> SearchAsync(ApplicationSearch criteria, CancellationToken cancellationToken = default);

    /// <summary>Returns null when the application does not exist or is outside <paramref name="borrowerScope"/>.</summary>
    Task<ApplicationDetail?> GetDetailAsync(int applicationId, int? borrowerScope, CancellationToken cancellationToken = default);
}

public interface IBorrowerQueries
{
    /// <summary>The borrower linked to a Borrower-role user, or null.</summary>
    Task<BorrowerSummary?> GetByLinkedUserIdAsync(string userId, CancellationToken cancellationToken = default);
}

public interface IDashboardQueries
{
    Task<DashboardSummary> GetSummaryAsync(int? borrowerScope, CancellationToken cancellationToken = default);
}
