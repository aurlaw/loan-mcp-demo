using Microsoft.EntityFrameworkCore;

namespace Loans.Core.Data.Queries;

internal sealed class UserQueries(LoansDbContext db, TimeProvider timeProvider) : IUserQueries
{
    public async Task<IReadOnlyList<UserSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var users = await db.Users
            .AsNoTracking()
            .OrderBy(u => u.DisplayName)
            .Select(u => new { u.Id, u.DisplayName, u.Email, u.LockoutEnd })
            .ToListAsync(cancellationToken);

        var roles = await (
                from ur in db.UserRoles.AsNoTracking()
                join r in db.Roles.AsNoTracking() on ur.RoleId equals r.Id
                select new { ur.UserId, RoleName = r.Name! })
            .ToListAsync(cancellationToken);

        var linkedBorrowers = await db.Borrowers
            .AsNoTracking()
            .Where(b => b.LinkedUserId != null)
            .Select(b => new { UserId = b.LinkedUserId!, b.LegalName })
            .ToListAsync(cancellationToken);

        var rolesByUser = roles.ToLookup(r => r.UserId, r => r.RoleName);
        var borrowerByUser = linkedBorrowers.ToDictionary(b => b.UserId, b => b.LegalName);
        var now = timeProvider.GetUtcNow();

        return users
            .Select(u => new UserSummary(
                u.Id,
                u.DisplayName,
                u.Email ?? string.Empty,
                rolesByUser[u.Id].Order(StringComparer.Ordinal).ToList(),
                borrowerByUser.GetValueOrDefault(u.Id),
                u.LockoutEnd is { } end && end > now))
            .ToList();
    }
}
