using Loans.Core.Data;
using Loans.Core.Data.Seeding;
using Loans.Core.Domain;
using Loans.Core.Engine;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Loans.Core.Tests.Data;

public class SeedingTests(SeededDatabaseFixture fixture) : IClassFixture<SeededDatabaseFixture>
{
    private TestDatabase Db => fixture.Database;

    [Fact]
    public async Task Seed_CreatesRolesUsersAndDomainData()
    {
        await using var scope = Db.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LoansDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        Assert.Equal(SeedRoles.All.Order(), (await db.Roles.Select(r => r.Name!).ToListAsync()).Order());
        Assert.Equal(4, await db.Users.CountAsync());

        foreach (var seed in SeedUsers.All)
        {
            var user = await users.FindByEmailAsync(seed.Email);
            Assert.NotNull(user);
            Assert.True(user.EmailConfirmed);
            Assert.Equal(seed.DisplayName, user.DisplayName);
            Assert.Equal([seed.Role], await users.GetRolesAsync(user));
            Assert.True(await users.CheckPasswordAsync(user, TestDatabase.Password));
        }

        Assert.Equal(8, await db.Borrowers.CountAsync());
        Assert.Equal(6, await db.Applications.CountAsync());
        Assert.Equal(10, await db.Loans.CountAsync());
    }

    [Fact]
    public async Task LinkedBorrower_IsLinkedToBorrowerUser_AndHasLoanAndApplication()
    {
        await using var scope = Db.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LoansDbContext>();
        var user = await db.Users.SingleAsync(u => u.Email == SeedUsers.Borrower.Email);

        var borrower = await db.Borrowers.SingleAsync(b => b.LinkedUserId == user.Id);

        Assert.Equal(SeedBorrowers.Linked, borrower.LegalName);
        Assert.True(await db.Loans.AnyAsync(l => l.BorrowerId == borrower.Id));
        Assert.True(await db.Applications.AnyAsync(a => a.BorrowerId == borrower.Id));
    }

    [Theory]
    [InlineData(SeedBorrowers.AlleghenyPrecision, Recommendation.Approve, ApplicationStatus.Submitted)]
    [InlineData(SeedBorrowers.EmberAndOak, Recommendation.ApproveWithConditions, ApplicationStatus.Submitted)]
    [InlineData(SeedBorrowers.KeystoneFreight, Recommendation.ApproveWithConditions, ApplicationStatus.Submitted)]
    [InlineData(SeedBorrowers.MonongahelaProperty, Recommendation.Decline, ApplicationStatus.Submitted)]
    [InlineData(SeedBorrowers.IronworksBrewing, Recommendation.ApproveWithConditions, ApplicationStatus.InUnderwriting)]
    [InlineData(SeedBorrowers.RidgelineBuilders, Recommendation.Decline, ApplicationStatus.Declined)]
    public async Task SeededApplications_ProduceEngineeredRecommendations(string borrowerName, Recommendation expected, ApplicationStatus expectedStatus)
    {
        var (application, borrower) = await LoadApplicationAsync(borrowerName);

        var outcome = UnderwritingEngine.Evaluate(application, borrower);

        Assert.Equal(expected, outcome.Recommendation);
        Assert.Equal(expectedStatus, application.Status);
    }

    [Fact]
    public async Task SeededApplications_HitIntendedExceptions()
    {
        async Task<UnderwritingOutcome> Evaluate(string name)
        {
            var (a, b) = await LoadApplicationAsync(name);
            return UnderwritingEngine.Evaluate(a, b);
        }

        var a1 = await Evaluate(SeedBorrowers.AlleghenyPrecision);
        Assert.Empty(a1.Exceptions);
        Assert.Equal(150, a1.DscrX100);
        Assert.Equal(6500, a1.LtvBps);

        Assert.Equal([PolicyException.DscrBelowMinimum], (await Evaluate(SeedBorrowers.EmberAndOak)).Exceptions);
        Assert.Equal([PolicyException.LtvAboveMaximum], (await Evaluate(SeedBorrowers.KeystoneFreight)).Exceptions);
        Assert.Equal([PolicyException.DscrBelowMinimum, PolicyException.LtvAboveMaximum], (await Evaluate(SeedBorrowers.MonongahelaProperty)).Exceptions);

        var a5 = await Evaluate(SeedBorrowers.IronworksBrewing);
        Assert.Equal([PolicyException.InsufficientTimeInBusiness], a5.Exceptions);
        Assert.Null(a5.LtvBps);

        Assert.True((await Evaluate(SeedBorrowers.RidgelineBuilders)).DscrX100 < 100);
    }

    [Fact]
    public async Task A5_IsInUnderwritingWithResultAndNoDecision()
    {
        var (application, _) = await LoadApplicationAsync(SeedBorrowers.IronworksBrewing);

        Assert.Equal(ApplicationStatus.InUnderwriting, application.Status);
        Assert.NotNull(application.UnderwritingResult);
        Assert.Equal(Recommendation.ApproveWithConditions, application.UnderwritingResult.Recommendation);
        Assert.Null(application.Decision);
    }

    [Fact]
    public async Task A6_IsDeclinedWithResultAndDecision_AndRefusesRerun()
    {
        var (application, borrower) = await LoadApplicationAsync(SeedBorrowers.RidgelineBuilders);

        Assert.Equal(ApplicationStatus.Declined, application.Status);
        Assert.NotNull(application.UnderwritingResult);
        Assert.NotNull(application.Decision);
        Assert.Equal(DecisionOutcome.Decline, application.Decision.Outcome);

        var outcome = UnderwritingEngine.Evaluate(application, borrower);
        var rerun = TestData.Ok(UnderwritingResult.Create(
            outcome.DscrX100, outcome.LtvBps, outcome.RiskRating, outcome.Recommendation, outcome.Exceptions,
            outcome.ProposedMonthlyPaymentCents, TestDatabase.Now, "someone"));

        var result = application.ApplyUnderwriting(rerun);

        Assert.True(result.IsFailure);
        Assert.Equal("underwriting.decision_exists", result.Error.Code);
    }

    [Fact]
    public async Task LoanStatuses_AreConsistentWithPaymentHistory()
    {
        await using var scope = Db.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LoansDbContext>();
        var loans = await db.Loans.AsNoTracking().Include(l => l.Payments).ToListAsync();

        Assert.All(loans, l => Assert.All(l.Payments, p => Assert.True(p.DueDate <= TestDatabase.Anchor)));

        var pastDue = loans.Where(l => l.Status == LoanStatus.PastDue).ToList();
        Assert.Equal(2, pastDue.Count);
        Assert.All(pastDue, l =>
        {
            Assert.Contains(l.Payments, p => p.Status == PaymentStatus.Missed);
            Assert.Contains(l.Payments, p => p.Status == PaymentStatus.Late && p.PaidDate > p.DueDate);
            Assert.Equal(PaymentStatus.Missed, l.Payments.MaxBy(p => p.DueDate)!.Status);
        });

        var paidOff = Assert.Single(loans, l => l.Status == LoanStatus.PaidOff);
        Assert.Equal(0, BalanceCalculator.CurrentBalanceCents(paidOff));
        Assert.Equal(paidOff.TermMonths, paidOff.Payments.Count);

        var current = loans.Where(l => l.Status == LoanStatus.Current).ToList();
        Assert.Equal(7, current.Count);
        Assert.All(current, l =>
        {
            Assert.DoesNotContain(l.Payments, p => p.Status == PaymentStatus.Missed);
            Assert.InRange(l.Payments.Count, 12, 36);
        });

        Assert.True(loans.Count(l => l.Product == LoanProduct.CreMortgage && l.TermMonths < l.AmortizationMonths) >= 2);
        Assert.Equal(Enumerable.Range(10001, 10).Select(n => $"CL-{n}"), loans.Select(l => l.LoanNumber).Order());
    }

    [Fact]
    public async Task SeedingTwice_ChangesNothing()
    {
        await using var db = await TestDatabase.CreateSeededAsync();

        var before = await CountsAsync(db);
        await db.InitializeAsync();
        var after = await CountsAsync(db);

        Assert.Equal(before, after);
    }

    [Fact]
    public async Task MissingPassword_WhenUsersMustBeCreated_FailsWithClearMessage()
    {
        await using var db = TestDatabase.CreateUninitialized(seedPassword: null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => db.InitializeAsync());

        Assert.Contains("Seed:DefaultPassword", ex.Message);
    }

    [Fact]
    public async Task MissingPassword_AfterUsersExist_DoesNotFail()
    {
        await using var seeded = await TestDatabase.CreateSeededAsync();
        await using var noPassword = seeded.BuildProvider(seedPassword: null);
        await using var scope = noPassword.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<LoansDatabaseInitializer>().InitializeAsync(resetDatabase: false);

        Assert.Equal(4, await scope.ServiceProvider.GetRequiredService<LoansDbContext>().Users.CountAsync());
    }

    [Fact]
    public async Task Reset_RecreatesAndReseeds()
    {
        await using var db = await TestDatabase.CreateSeededFileAsync();
        await db.WithScopeAsync(async sp =>
        {
            var ctx = sp.GetRequiredService<LoansDbContext>();
            ctx.Payments.RemoveRange(ctx.Payments);
            return await ctx.SaveChangesAsync();
        });

        await db.InitializeAsync(reset: true);

        Assert.True(await db.WithScopeAsync(sp => sp.GetRequiredService<LoansDbContext>().Payments.AnyAsync()));
        Assert.Equal(10, await db.WithScopeAsync(sp => sp.GetRequiredService<LoansDbContext>().Loans.CountAsync()));
    }

    private async Task<(Application Application, Borrower Borrower)> LoadApplicationAsync(string borrowerName)
    {
        await using var scope = Db.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LoansDbContext>();
        var borrower = await db.Borrowers.AsNoTracking().SingleAsync(b => b.LegalName == borrowerName);
        var application = await db.Applications.AsNoTracking().SingleAsync(a => a.BorrowerId == borrower.Id);
        return (application, borrower);
    }

    private static Task<(int, int, int, int, int, int, int)> CountsAsync(TestDatabase db) =>
        db.WithScopeAsync(async sp =>
        {
            var ctx = sp.GetRequiredService<LoansDbContext>();
            return (
                await ctx.Roles.CountAsync(),
                await ctx.Users.CountAsync(),
                await ctx.UserRoles.CountAsync(),
                await ctx.Borrowers.CountAsync(),
                await ctx.Applications.CountAsync(),
                await ctx.Loans.CountAsync(),
                await ctx.Payments.CountAsync());
        });
}
