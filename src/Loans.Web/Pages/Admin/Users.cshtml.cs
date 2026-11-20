using Loans.Core.Data.Queries;
using Loans.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Loans.Web.Pages.Admin;

[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public class UsersModel(IUserQueries users) : PageModel
{
    public IReadOnlyList<UserSummary> Users { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Users = await users.ListAsync(cancellationToken);
    }
}
