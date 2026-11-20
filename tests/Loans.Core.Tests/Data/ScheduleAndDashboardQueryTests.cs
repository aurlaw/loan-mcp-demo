using Loans.Core.Data;
using Loans.Core.Data.Queries;
using Loans.Core.Data.Seeding;
using Loans.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Loans.Core.Tests.Data;

public class ScheduleAndDashboardQueryTests(SeededDatabaseFixture fixture) : IClassFixture<SeededDatabaseFixture>
{
    private TestDatabase Db => fixture.Database;

    private Task<T> Loans<T>(Func<ILoanQueries, Task<T>> f) => Db.WithScopeAsync(sp => f(sp.GetRequiredService<ILoanQueries>()));
    private Task<DashboardSummary> Dashboard(int? scope) => Db.WithScopeAsync(sp => sp.GetRequiredService<IDashboardQueries>().GetSummaryAsync(scope));

    private Task<int> LoanIdAsync(string loanNumber) =>
        Db.WithScopeAsync(sp => sp.GetRequiredService<LoansDbContext>().Loans.Where(l => l.LoanNumber == loanNumber).Select(l => l.Id).SingleAsync());

    private Task<int> BorrowerIdAsync(string legalName) =>
        Db.WithScopeAsync(sp => sp.GetRequiredService<LoansDbContext>().Borrowers.Where(b => b.LegalName == legalName).Select(b => b.Id).SingleAsync());

    [Fact]
    public async Task Schedule_CreBalloon_HasFullTermWithBalloonAndPaidRowsMarked()
    {
        var id = await LoanIdAsync("CL-10002"); // CRE 120/300, 36 paid

        var schedule = await Loans(q => q.GetScheduleAsync(id, null));

        Assert.NotNull(schedule);
        Assert.Equal(120, schedule.Rows.Count);
        Assert.True(schedule.BalloonCents > 0);
        var last = schedule.Rows[^1];
        Assert.True(last.IsBalloon);
        Assert.Equal(schedule.MonthlyPaymentCents + schedule.BalloonCents, last.PaymentCents);
        Assert.Equal(0, last.RemainingBalanceCents);
        Assert.Single(schedule.Rows, r => r.IsBalloon);
        Assert.Equal(36, schedule.Rows.Count(r => r.IsPaid));
        Assert.All(schedule.Rows.Take(36), r => Assert.True(r.IsPaid));
        Assert.Equal(TestDatabase.Anchor, schedule.Rows[35].DueDate);
    }

    [Fact]
    public async Task Schedule_FullyAmortizing_HasNoBalloon_AndMissedRowsNotPaid()
    {
        var id = await LoanIdAsync("CL-10004"); // TermLoan 84/84, 20 installments, last 2 missed

        var schedule = await Loans(q => q.GetScheduleAsync(id, null));

        Assert.NotNull(schedule);
        Assert.Equal(84, schedule.Rows.Count);
        Assert.Equal(0, schedule.BalloonCents);
        Assert.DoesNotContain(schedule.Rows, r => r.IsBalloon);
        Assert.Equal(18, schedule.Rows.Count(r => r.IsPaid));
        Assert.False(schedule.Rows[18].IsPaid);
        Assert.False(schedule.Rows[19].IsPaid);
    }

    [Fact]
    public async Task Schedule_OutOfScope_ReturnsNull()
    {
        var otherLoan = await LoanIdAsync("CL-10003");
        var linkedId = await BorrowerIdAsync(SeedBorrowers.Linked);

        Assert.Null(await Loans(q => q.GetScheduleAsync(otherLoan, linkedId)));
        Assert.Null(await Loans(q => q.GetScheduleAsync(99_999, null)));
        var ownLoan = await LoanIdAsync("CL-10001");
        Assert.NotNull(await Loans(q => q.GetScheduleAsync(ownLoan, linkedId)));
    }

    [Fact]
    public async Task PayoffQuote_ValidDate_ReturnsEngineQuote()
    {
        var id = await LoanIdAsync("CL-10001");
        var detail = await Loans(q => q.GetDetailAsync(id, null));

        var result = await Loans(q => q.GetPayoffQuoteAsync(id, TestDatabase.Anchor.AddDays(15), null));

        var quote = TestData.Ok(result);
        Assert.Equal(detail!.CurrentBalanceCents, quote.PrincipalBalanceCents);
        Assert.Equal(TestDatabase.Anchor, quote.PaidThroughDate);
        Assert.Equal(15, quote.Days);
        Assert.True(quote.AccruedInterestCents > 0);
        Assert.Equal(quote.PrincipalBalanceCents + quote.AccruedInterestCents, quote.TotalPayoffCents);
        Assert.Equal("CL-10001", quote.LoanNumber);
    }

    [Fact]
    public async Task PayoffQuote_PropagatesEngineErrors()
    {
        var current = await LoanIdAsync("CL-10001");
        var paidOff = await LoanIdAsync("CL-10007");

        var beforePaidThrough = await Loans(q => q.GetPayoffQuoteAsync(current, TestDatabase.Anchor.AddDays(-1), null));
        var alreadyPaidOff = await Loans(q => q.GetPayoffQuoteAsync(paidOff, TestDatabase.Anchor, null));

        Assert.Equal("payoff.date_before_paid_through", beforePaidThrough.Error?.Code);
        Assert.Equal("payoff.loan_paid_off", alreadyPaidOff.Error?.Code);
    }

    [Fact]
    public async Task PayoffQuote_OutOfScope_IsNotFound()
    {
        var otherLoan = await LoanIdAsync("CL-10003");
        var linkedId = await BorrowerIdAsync(SeedBorrowers.Linked);

        var result = await Loans(q => q.GetPayoffQuoteAsync(otherLoan, TestDatabase.Anchor, linkedId));

        Assert.Equal(QueryErrors.LoanNotFound, result.Error?.Code);
    }

    [Fact]
    public async Task Dashboard_Global_MatchesSeed()
    {
        var summary = await Dashboard(null);

        Assert.Equal(7, summary.LoansByStatus[LoanStatus.Current]);
        Assert.Equal(2, summary.LoansByStatus[LoanStatus.PastDue]);
        Assert.Equal(1, summary.LoansByStatus[LoanStatus.PaidOff]);
        Assert.Equal(4, summary.ApplicationsByStatus[ApplicationStatus.Submitted]);
        Assert.Equal(1, summary.ApplicationsByStatus[ApplicationStatus.InUnderwriting]);
        Assert.Equal(0, summary.ApplicationsByStatus[ApplicationStatus.Approved]);
        Assert.Equal(1, summary.ApplicationsByStatus[ApplicationStatus.Declined]);

        Assert.Equal(["CL-10004", "CL-10006"], summary.PastDueLoans.Select(p => p.LoanNumber).Order());
        Assert.All(summary.PastDueLoans, p => Assert.NotNull(p.OldestMissedDueDate));

        var allLoans = await Loans(q => q.SearchAsync(new LoanSearch(PageSize: 100)));
        Assert.Equal(allLoans.Items.Sum(l => l.CurrentBalanceCents), summary.TotalOutstandingCents);
    }

    [Fact]
    public async Task Dashboard_BorrowerScope_CountsOnlyThatBorrower()
    {
        var linkedId = await BorrowerIdAsync(SeedBorrowers.Linked);

        var summary = await Dashboard(linkedId);

        Assert.Equal(1, summary.LoansByStatus.Values.Sum());
        Assert.Equal(1, summary.ApplicationsByStatus.Values.Sum());
        Assert.Empty(summary.PastDueLoans);

        var empty = await Dashboard(0);
        Assert.Equal(0, empty.LoansByStatus.Values.Sum());
        Assert.Equal(0, empty.TotalOutstandingCents);
    }
}
