using Loans.Core.Data.Queries;
using Loans.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Loans.Web.Pages.Applications;

public class DetailsModel(ICurrentUserScope currentUserScope, IApplicationQueries applications) : PageModel
{
    public ApplicationDetail Application { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var scope = (await currentUserScope.GetAsync(cancellationToken)).QueryScope;
        var detail = await applications.GetDetailAsync(id, scope, cancellationToken);
        if (detail is null)
        {
            return NotFound();
        }

        Application = detail;
        return Page();
    }
}
