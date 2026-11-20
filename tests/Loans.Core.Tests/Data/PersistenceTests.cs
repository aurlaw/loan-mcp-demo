using Loans.Core.Data;
using Loans.Core.Data.Seeding;
using Loans.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Loans.Core.Tests.Data;

public class PersistenceTests
{
    [Fact]
    public async Task OwnedTypes_RoundTrip()
    {
        await using var testDb = await TestDatabase.CreateSeededAsync();
        var runAt = new DateTimeOffset(2026, 11, 18, 9, 30, 0, TimeSpan.Zero);
        var decidedAt = new DateTimeOffset(2026, 11, 19, 16, 45, 0, TimeSpan.Zero);
        int applicationId;

        await using (var scope = testDb.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LoansDbContext>();
            var borrower = await db.Borrowers.FirstAsync();
            var officer = await db.Users.SingleAsync(u => u.Email == SeedUsers.Officer.Email);
            var underwriter = await db.Users.SingleAsync(u => u.Email == SeedUsers.Underwriter.Email);

            var application = TestData.Ok(Application.Create(
                borrower.Id, officer.Id, LoanProduct.CreMortgage, 90_000_000, 735, 120, 300, "Round-trip test",
                new DateOnly(2026, 11, 10),
                TestData.Ok(Financials.Create(300_000_000, 12_345_678, 1_111_111)),
                TestData.Ok(Collateral.Create(CollateralType.RealEstate, 110_000_000))));
            db.Applications.Add(application);
            await db.SaveChangesAsync();

            Assert.True(application.ApplyUnderwriting(TestData.Ok(UnderwritingResult.Create(
                118, 8182, 7, Recommendation.Decline,
                [PolicyException.DscrBelowMinimum, PolicyException.LtvAboveMaximum],
                654_321, runAt, underwriter.Id))).IsSuccess);
            Assert.True(application.RecordDecision(DecisionOutcome.ApproveWithConditions, 760, "Personal guarantee; quarterly financials", underwriter.Id, decidedAt).IsSuccess);
            await db.SaveChangesAsync();
            applicationId = application.Id;
        }

        await using (var scope = testDb.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LoansDbContext>();
            var loaded = await db.Applications.AsNoTracking().SingleAsync(a => a.Id == applicationId);

            Assert.Equal(300_000_000, loaded.Financials.AnnualRevenueCents);
            Assert.Equal(12_345_678, loaded.Financials.NetOperatingIncomeCents);
            Assert.Equal(1_111_111, loaded.Financials.ExistingAnnualDebtServiceCents);
            Assert.Equal(CollateralType.RealEstate, loaded.Collateral.Type);
            Assert.Equal(110_000_000, loaded.Collateral.AppraisedValueCents);

            var result = Assert.IsType<UnderwritingResult>(loaded.UnderwritingResult);
            Assert.Equal(applicationId, result.ApplicationId);
            Assert.Equal(118, result.DscrX100);
            Assert.Equal(8182, result.LtvBps);
            Assert.Equal(7, result.RiskRating);
            Assert.Equal(Recommendation.Decline, result.Recommendation);
            Assert.Equal([PolicyException.DscrBelowMinimum, PolicyException.LtvAboveMaximum], result.Exceptions);
            Assert.Equal(654_321, result.ProposedMonthlyPaymentCents);
            Assert.Equal(runAt, result.RunAt);

            var decision = Assert.IsType<Decision>(loaded.Decision);
            Assert.Equal(applicationId, decision.ApplicationId);
            Assert.Equal(DecisionOutcome.ApproveWithConditions, decision.Outcome);
            Assert.Equal(760, decision.ApprovedRateBps);
            Assert.Equal("Personal guarantee; quarterly financials", decision.Conditions);
            Assert.Equal(decidedAt, decision.DecidedAt);
            Assert.Equal(ApplicationStatus.Approved, loaded.Status);
        }
    }

    [Fact]
    public async Task EmptyExceptionsList_RoundTrips()
    {
        await using var testDb = await TestDatabase.CreateSeededAsync();
        var id = await ApplyToA1Async(testDb, dscr: 150, exceptions: []);

        var loaded = await testDb.WithScopeAsync(sp =>
            sp.GetRequiredService<LoansDbContext>().Applications.AsNoTracking().SingleAsync(a => a.Id == id));

        Assert.Empty(loaded.UnderwritingResult!.Exceptions);
    }

    [Fact]
    public async Task Rerun_ReplacesResultInPlace_InSameContext()
    {
        await using var testDb = await TestDatabase.CreateSeededAsync();
        int applicationId;

        await using (var scope = testDb.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LoansDbContext>();
            var application = await GetA1Async(db);
            applicationId = application.Id;

            Assert.True(application.ApplyUnderwriting(Result(150, [])).IsSuccess);
            await db.SaveChangesAsync();

            Assert.True(application.ApplyUnderwriting(Result(112, [PolicyException.DscrBelowMinimum])).IsSuccess);
            await db.SaveChangesAsync();
        }

        await AssertSingleResultAsync(testDb, applicationId, 112, [PolicyException.DscrBelowMinimum]);
    }

    [Fact]
    public async Task Rerun_ReplacesResultInPlace_AcrossContexts()
    {
        await using var testDb = await TestDatabase.CreateSeededAsync();
        var applicationId = await ApplyToA1Async(testDb, dscr: 150, exceptions: []);
        await ApplyToA1Async(testDb, dscr: 131, exceptions: [PolicyException.InsufficientTimeInBusiness]);

        await AssertSingleResultAsync(testDb, applicationId, 131, [PolicyException.InsufficientTimeInBusiness]);
    }

    [Fact]
    public async Task DateTimeOffset_CanBeOrderedAndFilteredOnSqlite()
    {
        await using var testDb = await TestDatabase.CreateSeededAsync();

        var ordered = await testDb.WithScopeAsync(sp =>
        {
            var db = sp.GetRequiredService<LoansDbContext>();
            return (
                from a in db.Applications
                join b in db.Borrowers on a.BorrowerId equals b.Id
                where a.UnderwritingResult != null && a.UnderwritingResult.RunAt > new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
                orderby a.UnderwritingResult!.RunAt
                select new { b.LegalName, a.UnderwritingResult!.RunAt }).ToListAsync();
        });

        Assert.Equal([SeedBorrowers.RidgelineBuilders, SeedBorrowers.IronworksBrewing], ordered.Select(x => x.LegalName));
        Assert.True(ordered[0].RunAt < ordered[1].RunAt);
    }

    private static UnderwritingResult Result(int dscr, PolicyException[] exceptions) =>
        TestData.Ok(UnderwritingResult.Create(dscr, 6500, 4, Recommendation.Approve, exceptions, 918_813, TestDatabase.Now, "underwriter"));

    private static async Task<Application> GetA1Async(LoansDbContext db)
    {
        var borrower = await db.Borrowers.SingleAsync(b => b.LegalName == SeedBorrowers.AlleghenyPrecision);
        return await db.Applications.SingleAsync(a => a.BorrowerId == borrower.Id);
    }

    private static Task<int> ApplyToA1Async(TestDatabase testDb, int dscr, PolicyException[] exceptions) =>
        testDb.WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<LoansDbContext>();
            var application = await GetA1Async(db);
            Assert.True(application.ApplyUnderwriting(Result(dscr, exceptions)).IsSuccess);
            await db.SaveChangesAsync();
            return application.Id;
        });

    private static async Task AssertSingleResultAsync(TestDatabase testDb, int applicationId, int expectedDscr, PolicyException[] expectedExceptions)
    {
        await using var scope = testDb.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LoansDbContext>();

        var rows = await db.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS \"Value\" FROM \"UnderwritingResults\" WHERE \"ApplicationId\" = {applicationId}")
            .SingleAsync();
        Assert.Equal(1, rows);

        var loaded = await db.Applications.AsNoTracking().SingleAsync(a => a.Id == applicationId);
        Assert.Equal(expectedDscr, loaded.UnderwritingResult!.DscrX100);
        Assert.Equal(expectedExceptions, loaded.UnderwritingResult.Exceptions);
        Assert.Equal(ApplicationStatus.InUnderwriting, loaded.Status);
    }
}
