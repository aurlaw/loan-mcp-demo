namespace Loans.Core.Data.Queries;

public interface ILoanQueries
{
    Task<PagedResult<LoanSummary>> SearchAsync(LoanSearch criteria, CancellationToken cancellationToken = default);

    /// <summary>Returns null when the loan does not exist or is outside <paramref name="borrowerScope"/>.</summary>
    Task<LoanDetail?> GetDetailAsync(int loanId, int? borrowerScope, CancellationToken cancellationToken = default);
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
