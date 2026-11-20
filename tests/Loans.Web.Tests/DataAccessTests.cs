using System.Net;
using Loans.Core.Data.Queries;
using Loans.Core.Data.Seeding;
using Loans.Web.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Loans.Web.Tests;

public class DataAccessTests(LoansWebFactory factory) : IClassFixture<LoansWebFactory>
{
    private static readonly string[] StaffEmails = [SeedUsers.Officer.Email, SeedUsers.Underwriter.Email, SeedUsers.Admin.Email];

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task Borrower_ListsShowOnlyOwnLoansAndApplications()
    {
        var client = await factory.SignedInClientAsync(SeedUsers.Borrower.Email);

        var loansHtml = await client.GetStringAsync("/Loans");
        var appsHtml = await client.GetStringAsync("/Applications");
        var dashboardHtml = await client.GetStringAsync("/");

        Assert.Equal(["CL-10001"], WebTestHelpers.LoanNumbersIn(loansHtml));
        Assert.Single(WebTestHelpers.ApplicationLinksIn(appsHtml));
        Assert.Contains(SeedBorrowers.Linked, appsHtml);
        Assert.DoesNotContain(SeedBorrowers.RidgelineBuilders, appsHtml);
        Assert.Equal(["CL-10001"], WebTestHelpers.LoanNumbersIn(dashboardHtml));
        Assert.DoesNotContain("CL-10004", dashboardHtml);
    }

    [Fact]
    public async Task Borrower_SearchCannotEscapeScope()
    {
        var client = await factory.SignedInClientAsync(SeedUsers.Borrower.Email);

        var html = await client.GetStringAsync("/Loans?q=Keystone");

        Assert.Empty(WebTestHelpers.LoanNumbersIn(html));
    }

    [Fact]
    public async Task Borrower_OtherBorrowersDetails_Return404()
    {
        var client = await factory.SignedInClientAsync(SeedUsers.Borrower.Email);
        var otherLoan = await factory.LoanIdAsync("CL-10003");
        var otherApp = await factory.ApplicationIdAsync(SeedBorrowers.RidgelineBuilders);
        var ownLoan = await factory.LoanIdAsync("CL-10001");
        var ownApp = await factory.ApplicationIdAsync(SeedBorrowers.Linked);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Loans/{otherLoan}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Loans/{otherLoan}?payoffDate={Today:yyyy-MM-dd}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Applications/{otherApp}")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/Loans/{ownLoan}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/Applications/{ownApp}")).StatusCode);
    }

    [Fact]
    public async Task Borrower_OtherBorrowersScheduleAndPayoff_AreNotFound()
    {
        // Schedule and payoff render only on the loan detail page; verify the scoped services behind it as well.
        var otherLoan = await factory.LoanIdAsync("CL-10003");
        var linkedBorrowerId = await factory.QueryDbAsync(db =>
            Task.FromResult(db.Borrowers.Single(b => b.LegalName == SeedBorrowers.Linked).Id));

        await using var scope = factory.Services.CreateAsyncScope();
        var loans = scope.ServiceProvider.GetRequiredService<ILoanQueries>();

        Assert.Null(await loans.GetScheduleAsync(otherLoan, linkedBorrowerId));
        Assert.Equal(QueryErrors.LoanNotFound, (await loans.GetPayoffQuoteAsync(otherLoan, Today, linkedBorrowerId)).Error?.Code);
    }

    [Fact]
    public async Task Staff_SeeAllLoansAndApplications()
    {
        foreach (var email in StaffEmails)
        {
            var client = await factory.SignedInClientAsync(email);

            var loans = WebTestHelpers.LoanNumbersIn(await client.GetStringAsync("/Loans"));
            var apps = WebTestHelpers.ApplicationLinksIn(await client.GetStringAsync("/Applications"));

            Assert.Equal(10, loans.Count);
            Assert.Equal(6, apps.Count);
        }
    }

    [Fact]
    public async Task Staff_DashboardShowsSeedCountsAndPastDueLoans()
    {
        var client = await factory.SignedInClientAsync(SeedUsers.Officer.Email);

        var html = await client.GetStringAsync("/");

        Assert.Contains("""data-testid="loans-Current">7<""", html);
        Assert.Contains("""data-testid="loans-PastDue">2<""", html);
        Assert.Contains("""data-testid="loans-PaidOff">1<""", html);
        Assert.Contains("""data-testid="applications-Submitted">4<""", html);
        Assert.Contains("""data-testid="applications-InUnderwriting">1<""", html);
        Assert.Contains("""data-testid="applications-Approved">0<""", html);
        Assert.Contains("""data-testid="applications-Declined">1<""", html);

        var pastDueSection = html[html.IndexOf("""data-testid="past-due""", StringComparison.Ordinal)..];
        Assert.Equal(["CL-10004", "CL-10006"], WebTestHelpers.LoanNumbersIn(pastDueSection).Order());
    }

    [Fact]
    public async Task LoanDetail_ShowsPayoffQuoteForValidDate()
    {
        var client = await factory.SignedInClientAsync(SeedUsers.Officer.Email);
        var loanId = await factory.LoanIdAsync("CL-10002");
        var payoffDate = Today.AddDays(10);

        PayoffQuoteDto expected;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<ILoanQueries>().GetPayoffQuoteAsync(loanId, payoffDate, null);
            Assert.True(result.IsSuccess);
            expected = result.Value;
        }

        var html = await client.GetStringAsync($"/Loans/{loanId}?payoffDate={payoffDate:yyyy-MM-dd}");

        Assert.Contains("""data-testid="payoff-quote""", html);
        Assert.Contains($"""data-testid="payoff-total">{Formatting.Money(expected.TotalPayoffCents)}<""", html);
        Assert.Contains(Formatting.Money(expected.AccruedInterestCents), html);
        Assert.DoesNotContain("""data-testid="quote-error""", html);
    }

    [Fact]
    public async Task LoanDetail_DateBeforePaidThrough_ShowsFriendlyError()
    {
        var client = await factory.SignedInClientAsync(SeedUsers.Officer.Email);
        var loanId = await factory.LoanIdAsync("CL-10002");

        var response = await client.GetAsync($"/Loans/{loanId}?payoffDate=2000-01-01");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("""data-testid="quote-error""", html);
        Assert.Contains("must be on or after the paid-through date", html);
    }

    [Fact]
    public async Task LoanDetail_PaidOffLoan_ShowsFriendlyMessage()
    {
        var client = await factory.SignedInClientAsync(SeedUsers.Officer.Email);
        var loanId = await factory.LoanIdAsync("CL-10007");

        var html = await client.GetStringAsync($"/Loans/{loanId}");

        Assert.Contains("This loan is paid off", html);
    }

    [Fact]
    public async Task LoanDetail_CreLoan_RendersHistoryAndScheduleWithBalloon()
    {
        var client = await factory.SignedInClientAsync(SeedUsers.Underwriter.Email);
        var loanId = await factory.LoanIdAsync("CL-10002");

        var html = await client.GetStringAsync($"/Loans/{loanId}");

        Assert.Contains("Payment history", html);
        Assert.Contains("Amortization schedule (120 payments)", html);
        Assert.Contains("schedule-balloon", html);
        Assert.Contains("Balloon at maturity", html);
        Assert.Contains("CRE mortgage", html);
        Assert.Contains("6.25% fixed", html);
    }

    [Fact]
    public async Task ApplicationDetail_ShowsUnderwritingAndDecision()
    {
        var client = await factory.SignedInClientAsync(SeedUsers.Underwriter.Email);
        var declined = await factory.ApplicationIdAsync(SeedBorrowers.RidgelineBuilders);
        var submitted = await factory.ApplicationIdAsync(SeedBorrowers.AlleghenyPrecision);

        var declinedHtml = await client.GetStringAsync($"/Applications/{declined}");
        var submittedHtml = await client.GetStringAsync($"/Applications/{submitted}");

        Assert.Contains("DSCR below minimum", declinedHtml);
        Assert.Contains("LTV above maximum", declinedHtml);
        Assert.Contains("0.74x", declinedHtml);
        Assert.Contains("83.34%", declinedHtml);
        Assert.Contains("Priya Raman", declinedHtml);
        Assert.DoesNotContain("No decision.", declinedHtml);

        Assert.Contains("Not yet underwritten.", submittedHtml);
        Assert.Contains("No decision.", submittedHtml);
        Assert.Contains("Marcus Hale", submittedHtml);
    }

    [Fact]
    public async Task Staff_UnknownIds_Return404()
    {
        var client = await factory.SignedInClientAsync(SeedUsers.Admin.Email);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Loans/99999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Applications/99999")).StatusCode);
    }
}
