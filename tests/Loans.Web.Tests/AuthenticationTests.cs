using System.Net;
using Loans.Core.Data.Seeding;
using Loans.Web.Pages.Account;

namespace Loans.Web.Tests;

public class AuthenticationTests(LoansWebFactory factory) : IClassFixture<LoansWebFactory>
{
    [Theory]
    [InlineData("/", "%2F")]
    [InlineData("/Loans", "%2FLoans")]
    [InlineData("/Applications/1", "%2FApplications%2F1")]
    public async Task Unauthenticated_PageRequests_RedirectToLogin(string path, string encodedReturnUrl)
    {
        var client = factory.CreateNonRedirectingClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.EndsWith($"/Account/Login?ReturnUrl={encodedReturnUrl}", response.Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/Account/Login")]
    [InlineData("/Account/AccessDenied")]
    [InlineData("/css/site.css")]
    public async Task AnonymousEndpoints_AreReachable(string path)
    {
        var client = factory.CreateNonRedirectingClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public static TheoryData<string, string, string> SeedUserData() => new()
    {
        { SeedUsers.Borrower.Email, SeedUsers.Borrower.DisplayName, "Borrower" },
        { SeedUsers.Officer.Email, SeedUsers.Officer.DisplayName, "Loan officer" },
        { SeedUsers.Underwriter.Email, SeedUsers.Underwriter.DisplayName, "Underwriter" },
        { SeedUsers.Admin.Email, SeedUsers.Admin.DisplayName, "Admin" },
    };

    [Theory]
    [MemberData(nameof(SeedUserData))]
    public async Task Login_SucceedsForEachSeededUser_AndHeaderShowsNameAndRole(string email, string displayName, string roleLabel)
    {
        var client = factory.CreateNonRedirectingClient();

        var response = await client.PostLoginAsync(email, LoansWebFactory.Password);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.OriginalString);

        var html = await client.GetStringAsync("/");
        // Layout elements carry a CSS-isolation attribute (b-xxxx), so match on data-testid.
        Assert.Equal(displayName, WebTestHelpers.TestIdText(html, "display-name"));
        Assert.Equal(roleLabel, WebTestHelpers.TestIdText(html, "role"));
        Assert.Contains("Sign out", html);
    }

    [Theory]
    [InlineData("officer@loans.test", "wrong-password")]
    [InlineData("nobody@loans.test", "Web-Test-Pa55!")]
    public async Task Login_Failure_ShowsGenericMessage_AndDoesNotSignIn(string email, string password)
    {
        var client = factory.CreateNonRedirectingClient();

        var response = await client.PostLoginAsync(email, password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(LoginModel.InvalidCredentialsMessage, await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/")).StatusCode);
    }

    [Theory]
    [InlineData("https://evil.example/phish", "/")]
    [InlineData("//evil.example", "/")]
    [InlineData("/Loans?status=PastDue", "/Loans?status=PastDue")]
    public async Task Login_OnlyHonorsLocalReturnUrls(string returnUrl, string expectedLocation)
    {
        var client = factory.CreateNonRedirectingClient();

        var response = await client.PostLoginAsync(SeedUsers.Officer.Email, LoansWebFactory.Password, returnUrl);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(expectedLocation, response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Logout_RequiresPostWithAntiforgery()
    {
        var client = await factory.SignedInClientAsync(SeedUsers.Officer.Email);

        var get = await client.GetAsync("/Account/Logout");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, get.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);

        var noToken = await client.PostAsync("/Account/Logout", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);

        var token = await client.GetAntiforgeryTokenAsync("/");
        var logout = await client.PostAsync("/Account/Logout", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));

        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/")).StatusCode);
    }
}
