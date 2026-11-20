using Loans.Core.Data;
using Loans.Core.Data.Queries;
using Loans.Core.Data.Seeding;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Loans.Core.Tests.Data;

public class UserLockoutTests
{
    [Fact]
    public async Task UserList_ReportsActiveLockout()
    {
        await using var testDb = await TestDatabase.CreateSeededAsync();
        await testDb.WithScopeAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var admin = await users.FindByEmailAsync(SeedUsers.Admin.Email);
            return await users.SetLockoutEndDateAsync(admin!, TestDatabase.Now.AddHours(1));
        });

        var list = await testDb.WithScopeAsync(sp => sp.GetRequiredService<IUserQueries>().ListAsync());

        Assert.True(list.Single(u => u.Email == SeedUsers.Admin.Email).IsLockedOut);
        Assert.Equal(1, list.Count(u => u.IsLockedOut));
    }
}
