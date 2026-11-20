using Loans.Core.Data.Queries;
using Loans.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Loans.Web.Pages;

public class IndexModel(
    ICurrentUserScope currentUserScope,
    IDashboardQueries dashboard,
    ILoanQueries loans,
    IApplicationQueries applications) : PageModel
{
    public UserScope Scope { get; private set; } = UserScope.Staff;
    public DashboardSummary Summary { get; private set; } = null!;
    public IReadOnlyList<LoanSummary> BorrowerLoans { get; private set; } = [];
    public IReadOnlyList<ApplicationSummary> BorrowerApplications { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Scope = await currentUserScope.GetAsync(cancellationToken);
        Summary = await dashboard.GetSummaryAsync(Scope.QueryScope, cancellationToken);

        if (Scope.IsBorrower)
        {
            BorrowerLoans = (await loans.SearchAsync(new LoanSearch(BorrowerId: Scope.QueryScope, PageSize: 100), cancellationToken)).Items;
            BorrowerApplications = (await applications.SearchAsync(new ApplicationSearch(BorrowerId: Scope.QueryScope, PageSize: 100), cancellationToken)).Items;
        }
    }
}
