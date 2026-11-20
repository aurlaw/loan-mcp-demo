using System.Security.Claims;
using Loans.Core.Data.Queries;
using Loans.Core.Data.Seeding;

namespace Loans.Web.Infrastructure;

/// <summary>Data scope for the current request.</summary>
/// <param name="IsBorrower">True when the user is in the Borrower role.</param>
/// <param name="BorrowerId">Linked borrower for Borrower users; null for staff or an unlinked borrower.</param>
/// <param name="BorrowerName">Linked borrower's legal name, if any.</param>
public sealed record UserScope(bool IsBorrower, int? BorrowerId, string? BorrowerName)
{
    /// <summary>Borrower ID that matches no rows; used for Borrower users without a linked profile.</summary>
    public const int NoBorrower = 0;

    public static readonly UserScope Staff = new(false, null, null);

    /// <summary>Value to pass to query services: null = global (staff), otherwise the borrower ID.</summary>
    public int? QueryScope => IsBorrower ? BorrowerId ?? NoBorrower : null;

    public bool HasNoBorrowerProfile => IsBorrower && BorrowerId is null;
}

public interface ICurrentUserScope
{
    Task<UserScope> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>Resolves (once per request) whether the user is staff (global) or a Borrower (own data only).</summary>
public sealed class CurrentUserScope(IHttpContextAccessor httpContextAccessor, IBorrowerQueries borrowers) : ICurrentUserScope
{
    private UserScope? _scope;

    public async Task<UserScope> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_scope is not null)
        {
            return _scope;
        }

        var user = httpContextAccessor.HttpContext?.User;
        var userId = user?.FindFirstValue(ClaimTypes.NameIdentifier);

        if (user?.Identity?.IsAuthenticated != true || userId is null)
        {
            // Fail closed: never global.
            return _scope = new UserScope(true, null, null);
        }

        if (!user.IsInRole(SeedRoles.Borrower))
        {
            return _scope = UserScope.Staff;
        }

        var borrower = await borrowers.GetByLinkedUserIdAsync(userId, cancellationToken);
        return _scope = new UserScope(true, borrower?.Id, borrower?.LegalName);
    }
}
