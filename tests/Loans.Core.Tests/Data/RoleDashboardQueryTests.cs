using Loans.Core.Data;
using Loans.Core.Data.Queries;
using Loans.Core.Data.Seeding;
using Loans.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Loans.Core.Tests.Data;

public class RoleDashboardQueryTests(SeededDatabaseFixture fixture) : IClassFixture<SeededDatabaseFixture>
{
    private TestDatabase Db => fixture.Database;

    private Task<T> Dashboard<T>(Func<IDashboardQueries, Task<T>> f) => Db.WithScopeAsync(sp => f(sp.GetRequiredService<IDashboardQueries>()));

    private Task<string> UserIdAsync(string email) =>
        Db.WithScopeAsync(sp => sp.GetRequiredService<LoansDbContext>().Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync());

    [Fact]
    public async Task OfficerPipeline_ListsAssignedApplications_NewestFirst()
    {
        var officerId = await UserIdAsync(SeedUsers.Officer.Email);

        var pipeline = await Dashboard(q => q.GetOfficerPipelineAsync(officerId));

        Assert.Equal(6, pipeline.Applications.Count);
        Assert.Equal(4, pipeline.CountsByStatus[ApplicationStatus.Submitted]);
        Assert.Equal(1, pipeline.CountsByStatus[ApplicationStatus.InUnderwriting]);
        Assert.Equal(0, pipeline.CountsByStatus[ApplicationStatus.Approved]);
        Assert.Equal(1, pipeline.CountsByStatus[ApplicationStatus.Declined]);
        Assert.Equal(pipeline.Applications.OrderByDescending(a => a.SubmittedOn).Select(a => a.Id), pipeline.Applications.Select(a => a.Id));
        Assert.Equal(SeedBorrowers.AlleghenyPrecision, pipeline.Applications[0].BorrowerName);
        Assert.Equal(Recommendation.Decline, pipeline.Applications.Single(a => a.Status == ApplicationStatus.Declined).Recommendation);
    }

    [Fact]
    public async Task OfficerPipeline_OtherUser_IsEmptyButZeroFilled()
    {
        var underwriterId = await UserIdAsync(SeedUsers.Underwriter.Email);

        var pipeline = await Dashboard(q => q.GetOfficerPipelineAsync(underwriterId));

        Assert.Empty(pipeline.Applications);
        Assert.Equal(Enum.GetValues<ApplicationStatus>().Length, pipeline.CountsByStatus.Count);
        Assert.All(pipeline.CountsByStatus.Values, c => Assert.Equal(0, c));
    }

    [Fact]
    public async Task UnderwritingQueue_MatchesSeed()
    {
        var queue = await Dashboard(q => q.GetUnderwritingQueueAsync());

        var awaiting = Assert.Single(queue.AwaitingDecision);
        Assert.Equal(SeedBorrowers.IronworksBrewing, awaiting.BorrowerName);
        Assert.Equal(160, awaiting.DscrX100);
        Assert.Null(awaiting.LtvBps);
        Assert.Equal(Recommendation.ApproveWithConditions, awaiting.Recommendation);

        // Oldest first: A4 (-15d), A3 (-12d), A2 (-9d), A1 (-6d).
        Assert.Equal(
            [SeedBorrowers.MonongahelaProperty, SeedBorrowers.KeystoneFreight, SeedBorrowers.EmberAndOak, SeedBorrowers.AlleghenyPrecision],
            queue.NotYetUnderwritten.Select(i => i.BorrowerName));

        var decided = Assert.Single(queue.RecentlyDecided);
        Assert.Equal(SeedBorrowers.RidgelineBuilders, decided.BorrowerName);
        Assert.Equal(DecisionOutcome.Decline, decided.Outcome);
        Assert.Equal(SeedUsers.Underwriter.DisplayName, decided.DecidedByName);
    }

    [Fact]
    public async Task UserList_ShowsAllUsersWithRolesAndLinkedBorrower()
    {
        var users = await Db.WithScopeAsync(sp => sp.GetRequiredService<IUserQueries>().ListAsync());

        Assert.Equal(4, users.Count);
        foreach (var seed in SeedUsers.All)
        {
            var user = Assert.Single(users, u => u.Email == seed.Email);
            Assert.Equal(seed.DisplayName, user.DisplayName);
            Assert.Equal([seed.Role], user.Roles);
            Assert.False(user.IsLockedOut);
            Assert.Equal(seed == SeedUsers.Borrower ? SeedBorrowers.Linked : null, user.LinkedBorrowerName);
        }
    }
}
