using System.Net;
using Loans.Core.Data.Seeding;

namespace Loans.Web.Tests;

public class RoleExperienceTests(LoansWebFactory factory) : IClassFixture<LoansWebFactory>
{
    [Fact]
    public async Task Officer_LeadsWithPipeline_ThenSnapshot()
    {
        var client = await factory.SignedInClientAsync(SeedUsers.Officer.Email);

        var html = await client.GetStringAsync("/");

        Assert.Contains("<h1 class=\"h3 mb-3\">My pipeline</h1>", html);
        var pipeline = WebTestHelpers.Section(html, "officer-pipeline");
        Assert.NotNull(pipeline);
        Assert.Equal(6, WebTestHelpers.ApplicationLinksIn(pipeline).Count);
        Assert.Equal("4", WebTestHelpers.TestIdText(pipeline, "pipeline-Submitted"));
        Assert.Equal("1", WebTestHelpers.TestIdText(pipeline, "pipeline-InUnderwriting"));
        Assert.Equal("1", WebTestHelpers.TestIdText(pipeline, "pipeline-Declined"));
        Assert.Equal("0", WebTestHelpers.TestIdText(pipeline, "pipeline-Approved"));

        var snapshot = WebTestHelpers.Section(html, "portfolio-snapshot");
        Assert.NotNull(snapshot);
        Assert.Equal("7", WebTestHelpers.TestIdText(snapshot, "snapshot-loans-Current"));
        Assert.Contains("2 past-due loans", snapshot);
        Assert.True(html.IndexOf("officer-pipeline", StringComparison.Ordinal) < html.IndexOf("portfolio-snapshot", StringComparison.Ordinal));

        Assert.False(WebTestHelpers.HasTestId(html, "underwriting-queue"));
        Assert.False(WebTestHelpers.HasTestId(html, "portfolio-overview"));
        Assert.False(WebTestHelpers.HasTestId(html, "users-link"));
    }

    [Fact]
    public async Task Underwriter_LeadsWithQueue_ThenSnapshot()
    {
        var client = await factory.SignedInClientAsync(SeedUsers.Underwriter.Email);

        var html = await client.GetStringAsync("/");

        Assert.Contains("<h1 class=\"h3 mb-3\">Underwriting queue</h1>", html);

        var awaiting = WebTestHelpers.Section(html, "awaiting-decision", "article");
        Assert.NotNull(awaiting);
        Assert.Single(WebTestHelpers.ApplicationLinksIn(awaiting));
        Assert.Contains(SeedBorrowers.IronworksBrewing, awaiting);

        var notYet = WebTestHelpers.Section(html, "not-yet-underwritten", "article");
        Assert.NotNull(notYet);
        Assert.Equal(4, WebTestHelpers.ApplicationLinksIn(notYet).Count);
        foreach (var name in new[] { SeedBorrowers.AlleghenyPrecision, SeedBorrowers.KeystoneFreight, SeedBorrowers.MonongahelaProperty })
        {
            Assert.Contains(name, notYet);
        }

        var decided = WebTestHelpers.Section(html, "recently-decided", "article");
        Assert.NotNull(decided);
        Assert.Contains(SeedBorrowers.RidgelineBuilders, decided);
        Assert.Contains(SeedUsers.Underwriter.DisplayName, decided);

        Assert.True(WebTestHelpers.HasTestId(html, "portfolio-snapshot"));
        Assert.False(WebTestHelpers.HasTestId(html, "officer-pipeline"));
        Assert.False(WebTestHelpers.HasTestId(html, "portfolio-overview"));
    }

    [Fact]
    public async Task Admin_LeadsWithPortfolioOverview_AndUsersLink()
    {
        var client = await factory.SignedInClientAsync(SeedUsers.Admin.Email);

        var html = await client.GetStringAsync("/");

        Assert.Contains("<h1 class=\"h3 mb-3\">Portfolio overview</h1>", html);
        Assert.True(WebTestHelpers.HasTestId(html, "portfolio-overview"));
        Assert.Contains("href=\"/Admin/Users\"", WebTestHelpers.Section(html, "users-link"));
        Assert.False(WebTestHelpers.HasTestId(html, "officer-pipeline"));
        Assert.False(WebTestHelpers.HasTestId(html, "underwriting-queue"));
        Assert.False(WebTestHelpers.HasTestId(html, "portfolio-snapshot"));
    }

    [Fact]
    public async Task Borrower_DashboardUnchanged()
    {
        var client = await factory.SignedInClientAsync(SeedUsers.Borrower.Email);

        var html = await client.GetStringAsync("/");

        Assert.Contains("Your loans", html);
        Assert.Contains(SeedBorrowers.Linked, html);
        Assert.Equal(["CL-10001"], WebTestHelpers.LoanNumbersIn(html));
        foreach (var testId in new[] { "officer-pipeline", "underwriting-queue", "portfolio-overview", "portfolio-snapshot", "users-link" })
        {
            Assert.False(WebTestHelpers.HasTestId(html, testId), testId);
        }
    }

    [Fact]
    public async Task UsersNavLink_IsAdminOnly()
    {
        var admin = await factory.SignedInClientAsync(SeedUsers.Admin.Email);
        Assert.Contains(">Users</a>", await admin.GetStringAsync("/Loans"));

        foreach (var email in new[] { SeedUsers.Officer.Email, SeedUsers.Underwriter.Email, SeedUsers.Borrower.Email })
        {
            var client = await factory.SignedInClientAsync(email);
            Assert.DoesNotContain(">Users</a>", await client.GetStringAsync("/Loans"));
        }
    }

    [Fact]
    public async Task UsersPage_Admin_ListsUsersWithRolesMcpAccessAndCapabilities()
    {
        var client = await factory.SignedInClientAsync(SeedUsers.Admin.Email);

        var response = await client.GetAsync("/Admin/Users");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Access is assigned by role. Capabilities determine what each user can do when connected through an AI assistant.", html);

        var expected = new (string Email, string Role, string Mcp, string[] Capabilities)[]
        {
            (SeedUsers.Borrower.Email, "Borrower", "No", []),
            (SeedUsers.Officer.Email, "Loan officer", "Yes", ["View loans and applications", "Run underwriting analysis"]),
            (SeedUsers.Underwriter.Email, "Underwriter", "Yes", ["View loans and applications", "Run underwriting analysis", "Record credit decisions"]),
            (SeedUsers.Admin.Email, "Admin", "Yes", ["View loans and applications"]),
        };

        foreach (var (email, role, mcp, capabilities) in expected)
        {
            var row = System.Text.RegularExpressions.Regex.Match(
                html, $"""<tr data-testid="user-{System.Text.RegularExpressions.Regex.Escape(email)}">(.*?)</tr>""",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            Assert.True(row.Success, email);
            var rowHtml = row.Groups[1].Value;

            Assert.Equal(role, WebTestHelpers.TestIdText(rowHtml, "user-role"));
            Assert.Contains($">{mcp}</span>", WebTestHelpers.Section(rowHtml, "user-mcp", "td"));
            var capabilityCell = WebTestHelpers.Section(rowHtml, "user-capabilities", "td")!;
            foreach (var label in capabilities)
            {
                Assert.Contains($"<li>{label}</li>", capabilityCell);
            }

            Assert.Equal(capabilities.Length, System.Text.RegularExpressions.Regex.Matches(capabilityCell, "<li>").Count);
        }

        Assert.Contains(SeedBorrowers.Linked, html);
    }

    [Theory]
    [InlineData("officer@loans.test")]
    [InlineData("underwriter@loans.test")]
    [InlineData("borrower@loans.test")]
    public async Task UsersPage_NonAdmin_IsForbidden(string email)
    {
        var client = await factory.SignedInClientAsync(email);

        var response = await client.GetAsync("/Admin/Users");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/AccessDenied", response.Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData("officer@loans.test", SeedBorrowers.AlleghenyPrecision, "Ready for underwriting analysis.")]
    [InlineData("officer@loans.test", SeedBorrowers.IronworksBrewing, "With underwriting — awaiting decision.")]
    [InlineData("officer@loans.test", SeedBorrowers.RidgelineBuilders, "Decision recorded.")]
    [InlineData("underwriter@loans.test", SeedBorrowers.AlleghenyPrecision, "Not yet underwritten.")]
    [InlineData("underwriter@loans.test", SeedBorrowers.IronworksBrewing, "Awaiting your credit decision.")]
    [InlineData("underwriter@loans.test", SeedBorrowers.RidgelineBuilders, "Decision recorded.")]
    [InlineData("admin@loans.test", SeedBorrowers.AlleghenyPrecision, "Status: Submitted.")]
    [InlineData("admin@loans.test", SeedBorrowers.IronworksBrewing, "Status: In underwriting.")]
    [InlineData("admin@loans.test", SeedBorrowers.RidgelineBuilders, "Status: Declined.")]
    [InlineData("borrower@loans.test", SeedBorrowers.AlleghenyPrecision, "Status: Submitted.")]
    public async Task ApplicationDetail_ShowsNextStepHintPerRoleAndStatus(string email, string borrowerName, string expected)
    {
        var client = await factory.SignedInClientAsync(email);
        var applicationId = await factory.ApplicationIdAsync(borrowerName);

        var html = await client.GetStringAsync($"/Applications/{applicationId}");

        Assert.Equal(expected, WebTestHelpers.TestIdText(html, "next-step"));
    }
}
