using Loans.Core.Data.Queries;
using Loans.Core.Domain;
using Loans.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Loans.Web.Pages.Loans;

public class IndexModel(ICurrentUserScope currentUserScope, ILoanQueries loans) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Query { get; set; }

    [BindProperty(SupportsGet = true)]
    public LoanStatus? Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public LoanProduct? Product { get; set; }

    [BindProperty(SupportsGet = true, Name = "page")]
    public int PageNumber { get; set; } = 1;

    public PagedResult<LoanSummary> Results { get; private set; } = null!;
    public Pager Pager { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var scope = await currentUserScope.GetAsync(cancellationToken);
        Results = await loans.SearchAsync(new LoanSearch(Query, Status, Product, scope.QueryScope, PageNumber), cancellationToken);
        Pager = new Pager("/Loans/Index", Results.Page, Results.PageSize, Results.TotalCount, new Dictionary<string, string?>
        {
            ["q"] = Query,
            ["status"] = Status?.ToString(),
            ["product"] = Product?.ToString(),
        });
    }
}
