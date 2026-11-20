using Loans.Core.Data.Queries;
using Loans.Core.Domain;
using Loans.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Loans.Web.Pages.Applications;

public class IndexModel(ICurrentUserScope currentUserScope, IApplicationQueries applications) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Query { get; set; }

    [BindProperty(SupportsGet = true)]
    public ApplicationStatus? Status { get; set; }

    [BindProperty(SupportsGet = true, Name = "page")]
    public int PageNumber { get; set; } = 1;

    public PagedResult<ApplicationSummary> Results { get; private set; } = null!;
    public Pager Pager { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var scope = await currentUserScope.GetAsync(cancellationToken);
        Results = await applications.SearchAsync(new ApplicationSearch(Query, Status, scope.QueryScope, PageNumber), cancellationToken);
        Pager = new Pager("/Applications/Index", Results.Page, Results.PageSize, Results.TotalCount, new Dictionary<string, string?>
        {
            ["q"] = Query,
            ["status"] = Status?.ToString(),
        });
    }
}
