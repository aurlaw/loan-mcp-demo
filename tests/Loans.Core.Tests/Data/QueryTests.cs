using Loans.Core.Data;
using Loans.Core.Data.Queries;
using Loans.Core.Data.Seeding;
using Loans.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Loans.Core.Tests.Data;

public class QueryTests(SeededDatabaseFixture fixture) : IClassFixture<SeededDatabaseFixture>
{
    private TestDatabase Db => fixture.Database;

    private Task<T> Loans<T>(Func<ILoanQueries, Task<T>> f) => Db.WithScopeAsync(sp => f(sp.GetRequiredService<ILoanQueries>()));
    private Task<T> Apps<T>(Func<IApplicationQueries, Task<T>> f) => Db.WithScopeAsync(sp => f(sp.GetRequiredService<IApplicationQueries>()));

    private Task<int> BorrowerIdAsync(string legalName) =>
        Db.WithScopeAsync(sp => sp.GetRequiredService<LoansDbContext>().Borrowers.Where(b => b.LegalName == legalName).Select(b => b.Id).SingleAsync());

    [Fact]
    public async Task BorrowerQueries_ResolveLinkedBorrower()
    {
        var userId = await Db.WithScopeAsync(sp =>
            sp.GetRequiredService<LoansDbContext>().Users.Where(u => u.Email == SeedUsers.Borrower.Email).Select(u => u.Id).SingleAsync());

        var borrower = await Db.WithScopeAsync(sp => sp.GetRequiredService<IBorrowerQueries>().GetByLinkedUserIdAsync(userId));

        Assert.NotNull(borrower);
        Assert.Equal(SeedBorrowers.Linked, borrower.LegalName);
        Assert.Null(await Db.WithScopeAsync(sp => sp.GetRequiredService<IBorrowerQueries>().GetByLinkedUserIdAsync("nobody")));
    }

    [Fact]
    public async Task LoanSearch_WithBorrowerScope_ReturnsOnlyThatBorrowersLoans()
    {
        var linkedId = await BorrowerIdAsync(SeedBorrowers.Linked);

        var result = await Loans(q => q.SearchAsync(new LoanSearch(BorrowerId: linkedId)));

        Assert.NotEmpty(result.Items);
        Assert.All(result.Items, l => Assert.Equal(linkedId, l.BorrowerId));
        Assert.Equal(result.Items.Count, result.TotalCount);
    }

    [Fact]
    public async Task LoanDetail_HonorsBorrowerScope()
    {
        var linkedId = await BorrowerIdAsync(SeedBorrowers.Linked);
        var otherId = await BorrowerIdAsync(SeedBorrowers.KeystoneFreight);
        var linkedLoan = (await Loans(q => q.SearchAsync(new LoanSearch(BorrowerId: linkedId)))).Items[0];
        var otherLoan = (await Loans(q => q.SearchAsync(new LoanSearch(BorrowerId: otherId)))).Items[0];

        Assert.NotNull(await Loans(q => q.GetDetailAsync(linkedLoan.Id, linkedId)));
        Assert.NotNull(await Loans(q => q.GetDetailAsync(linkedLoan.Id, null)));
        Assert.Null(await Loans(q => q.GetDetailAsync(otherLoan.Id, linkedId)));
        Assert.Null(await Loans(q => q.GetDetailAsync(linkedLoan.Id, otherId)));
        Assert.Null(await Loans(q => q.GetDetailAsync(99_999, null)));
    }

    [Fact]
    public async Task ApplicationSearchAndDetail_HonorBorrowerScope()
    {
        var linkedId = await BorrowerIdAsync(SeedBorrowers.Linked);
        var otherId = await BorrowerIdAsync(SeedBorrowers.RidgelineBuilders);

        var scoped = await Apps(q => q.SearchAsync(new ApplicationSearch(BorrowerId: linkedId)));
        var mine = Assert.Single(scoped.Items);
        Assert.Equal(SeedBorrowers.Linked, mine.BorrowerName);

        var others = await Apps(q => q.SearchAsync(new ApplicationSearch(BorrowerId: otherId)));
        var theirs = Assert.Single(others.Items);

        Assert.NotNull(await Apps(q => q.GetDetailAsync(mine.Id, linkedId)));
        Assert.Null(await Apps(q => q.GetDetailAsync(theirs.Id, linkedId)));
        Assert.Equal(6, (await Apps(q => q.SearchAsync(new ApplicationSearch()))).TotalCount);
    }

    [Theory]
    [InlineData("CL-10003", 1)]
    [InlineData("cl-1000", 9)]
    [InlineData("keystone", 2)]
    [InlineData("THREE RIVERS", 2)]
    [InlineData("no-such-loan", 0)]
    [InlineData("%", 0)]
    public async Task LoanSearch_TextMatchesLoanNumberOrBorrowerName_CaseInsensitive(string text, int expected)
    {
        var result = await Loans(q => q.SearchAsync(new LoanSearch(Text: text)));

        Assert.Equal(expected, result.TotalCount);
        Assert.Equal(expected, result.Items.Count);
    }

    [Fact]
    public async Task LoanSearch_FiltersByStatusAndProduct()
    {
        var pastDue = await Loans(q => q.SearchAsync(new LoanSearch(Status: LoanStatus.PastDue)));
        var cre = await Loans(q => q.SearchAsync(new LoanSearch(Product: LoanProduct.CreMortgage)));

        Assert.Equal(2, pastDue.TotalCount);
        Assert.All(pastDue.Items, l => Assert.Equal(LoanStatus.PastDue, l.Status));
        Assert.Equal(3, cre.TotalCount);
    }

    [Fact]
    public async Task LoanSearch_Pages()
    {
        var page1 = await Loans(q => q.SearchAsync(new LoanSearch(Page: 1, PageSize: 3)));
        var page4 = await Loans(q => q.SearchAsync(new LoanSearch(Page: 4, PageSize: 3)));
        var clamped = await Loans(q => q.SearchAsync(new LoanSearch(PageSize: 500)));
        var defaulted = await Loans(q => q.SearchAsync(new LoanSearch(Page: 0, PageSize: 0)));

        Assert.Equal(10, page1.TotalCount);
        Assert.Equal(["CL-10001", "CL-10002", "CL-10003"], page1.Items.Select(l => l.LoanNumber));
        Assert.Equal(["CL-10010"], page4.Items.Select(l => l.LoanNumber));
        Assert.Equal(100, clamped.PageSize);
        Assert.Equal((1, 25), (defaulted.Page, defaulted.PageSize));
    }

    [Fact]
    public async Task LoanQueries_ComputeBalancesFromPostedPayments()
    {
        var all = await Loans(q => q.SearchAsync(new LoanSearch()));

        var paidOff = Assert.Single(all.Items, l => l.Status == LoanStatus.PaidOff);
        Assert.Equal(0, paidOff.CurrentBalanceCents);
        Assert.All(all.Items.Where(l => l.Status != LoanStatus.PaidOff), l =>
            Assert.InRange(l.CurrentBalanceCents, 1, l.OriginalPrincipalCents - 1));

        var pastDue = all.Items.First(l => l.Status == LoanStatus.PastDue);
        var detail = await Loans(q => q.GetDetailAsync(pastDue.Id, null));
        Assert.NotNull(detail);

        var expected = detail.OriginalPrincipalCents - detail.Payments.Where(p => p.PaidDate != null).Sum(p => p.PrincipalCents);
        Assert.Equal(expected, detail.CurrentBalanceCents);
        Assert.Equal(pastDue.CurrentBalanceCents, detail.CurrentBalanceCents);
        Assert.Equal(detail.Payments.Where(p => p.PaidDate != null).Max(p => p.DueDate), detail.PaidThroughDate);
        Assert.Equal(detail.Payments.OrderByDescending(p => p.DueDate).Select(p => p.Id), detail.Payments.Select(p => p.Id));
    }

    [Theory]
    [InlineData("brewhouse", 1)]
    [InlineData("RIDGELINE", 1)]
    [InlineData("llc", 3)]
    public async Task ApplicationSearch_TextMatchesBorrowerNameOrPurpose(string text, int expected)
    {
        var result = await Apps(q => q.SearchAsync(new ApplicationSearch(Text: text)));

        Assert.Equal(expected, result.TotalCount);
    }

    [Fact]
    public async Task ApplicationSearch_FiltersByStatus_AndExposesRecommendation()
    {
        var declined = await Apps(q => q.SearchAsync(new ApplicationSearch(Status: ApplicationStatus.Declined)));
        var submitted = await Apps(q => q.SearchAsync(new ApplicationSearch(Status: ApplicationStatus.Submitted)));

        var a6 = Assert.Single(declined.Items);
        Assert.Equal(Recommendation.Decline, a6.Recommendation);
        Assert.Equal(4, submitted.TotalCount);
        Assert.All(submitted.Items, a => Assert.Null(a.Recommendation));
    }

    [Fact]
    public async Task ApplicationDetail_IncludesOwnedDataAndDisplayNames()
    {
        var a6Id = (await Apps(q => q.SearchAsync(new ApplicationSearch(Text: "Ridgeline")))).Items.Single().Id;

        var detail = await Apps(q => q.GetDetailAsync(a6Id, null));

        Assert.NotNull(detail);
        Assert.Equal(SeedBorrowers.RidgelineBuilders, detail.Borrower.LegalName);
        Assert.Equal(SeedUsers.Officer.DisplayName, detail.AssignedOfficerName);
        Assert.Equal(CollateralType.Equipment, detail.Collateral.Type);
        Assert.Equal(13_000_000, detail.Financials.NetOperatingIncomeCents);
        Assert.NotNull(detail.UnderwritingResult);
        Assert.Equal(SeedUsers.Underwriter.DisplayName, detail.UnderwritingResult.RunByName);
        Assert.Equal([PolicyException.DscrBelowMinimum, PolicyException.LtvAboveMaximum], detail.UnderwritingResult.Exceptions);
        Assert.NotNull(detail.Decision);
        Assert.Equal(DecisionOutcome.Decline, detail.Decision.Outcome);
        Assert.Equal(SeedUsers.Underwriter.DisplayName, detail.Decision.DecidedByName);
    }
}
