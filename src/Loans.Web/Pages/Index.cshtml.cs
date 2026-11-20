using System.Security.Claims;
using Loans.Core.Data.Queries;
using Loans.Core.Security;
using Loans.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Loans.Web.Pages;

public enum DashboardView
{
    Borrower,
    OfficerPipeline,
    UnderwritingQueue,
    PortfolioOverview,
}

public class IndexModel(
    ICurrentUserScope currentUserScope,
    IDashboardQueries dashboard,
    ILoanQueries loans,
    IApplicationQueries applications) : PageModel
{
    public DashboardView View { get; private set; }
    public UserScope Scope { get; private set; } = UserScope.Staff;
    public DashboardSummary Summary { get; private set; } = null!;
    public OfficerPipeline? Pipeline { get; private set; }
    public UnderwritingQueue? Queue { get; private set; }
    public IReadOnlyList<LoanSummary> BorrowerLoans { get; private set; } = [];
    public IReadOnlyList<ApplicationSummary> BorrowerApplications { get; private set; } = [];

    public string Heading => View switch
    {
        DashboardView.OfficerPipeline => "My pipeline",
        DashboardView.UnderwritingQueue => "Underwriting queue",
        DashboardView.PortfolioOverview => "Portfolio overview",
        _ => "Welcome",
    };

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Scope = await currentUserScope.GetAsync(cancellationToken);
        Summary = await dashboard.GetSummaryAsync(Scope.QueryScope, cancellationToken);
        View = SelectView(Scope, User.Capabilities());

        switch (View)
        {
            case DashboardView.Borrower:
                BorrowerLoans = (await loans.SearchAsync(new LoanSearch(BorrowerId: Scope.QueryScope, PageSize: 100), cancellationToken)).Items;
                BorrowerApplications = (await applications.SearchAsync(new ApplicationSearch(BorrowerId: Scope.QueryScope, PageSize: 100), cancellationToken)).Items;
                break;
            case DashboardView.OfficerPipeline:
                Pipeline = await dashboard.GetOfficerPipelineAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, cancellationToken);
                break;
            case DashboardView.UnderwritingQueue:
                Queue = await dashboard.GetUnderwritingQueueAsync(cancellationToken);
                break;
        }
    }

    /// <summary>Lead with what the user can act on: decisions → queue; underwriting → pipeline; read-only staff → portfolio.</summary>
    private static DashboardView SelectView(UserScope scope, IReadOnlySet<Capability> capabilities) =>
        scope.IsBorrower ? DashboardView.Borrower
        : capabilities.Contains(Capability.DecisionsWrite) ? DashboardView.UnderwritingQueue
        : capabilities.Contains(Capability.UnderwritingRun) ? DashboardView.OfficerPipeline
        : DashboardView.PortfolioOverview;
}
