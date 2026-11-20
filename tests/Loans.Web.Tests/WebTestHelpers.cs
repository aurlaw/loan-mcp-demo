using System.Net;
using System.Text.RegularExpressions;
using Loans.Core.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Loans.Web.Tests;

internal static partial class WebTestHelpers
{
    public static HttpClient CreateNonRedirectingClient(this LoansWebFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    public static async Task<string> GetAntiforgeryTokenAsync(this HttpClient client, string path)
    {
        var html = await client.GetStringAsync(path);
        var match = AntiforgeryInput().Match(html);
        Assert.True(match.Success, $"No antiforgery token found on {path}");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    public static async Task<HttpResponseMessage> PostLoginAsync(this HttpClient client, string email, string password, string? returnUrl = null)
    {
        var token = await client.GetAntiforgeryTokenAsync("/Account/Login");
        var form = new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password,
            ["__RequestVerificationToken"] = token,
        };
        if (returnUrl is not null)
        {
            form["ReturnUrl"] = returnUrl;
        }

        return await client.PostAsync("/Account/Login", new FormUrlEncodedContent(form));
    }

    public static async Task<HttpClient> SignedInClientAsync(this LoansWebFactory factory, string email)
    {
        var client = factory.CreateNonRedirectingClient();
        var response = await client.PostLoginAsync(email, LoansWebFactory.Password);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    public static async Task<T> QueryDbAsync<T>(this LoansWebFactory factory, Func<LoansDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<LoansDbContext>());
    }

    public static Task<int> LoanIdAsync(this LoansWebFactory factory, string loanNumber) =>
        factory.QueryDbAsync(db => db.Loans.Where(l => l.LoanNumber == loanNumber).Select(l => l.Id).SingleAsync());

    public static Task<int> ApplicationIdAsync(this LoansWebFactory factory, string borrowerName) =>
        factory.QueryDbAsync(db =>
            (from a in db.Applications
             join b in db.Borrowers on a.BorrowerId equals b.Id
             where b.LegalName == borrowerName
             select a.Id).SingleAsync());

    public static IReadOnlySet<string> LoanNumbersIn(string html) =>
        LoanNumber().Matches(html).Select(m => m.Value).ToHashSet();

    public static IReadOnlySet<string> ApplicationLinksIn(string html) =>
        ApplicationLink().Matches(html).Select(m => m.Value).ToHashSet();

    /// <summary>Inner text of the first element with the given data-testid.</summary>
    public static string? TestIdText(string html, string testId)
    {
        var match = Regex.Match(html, $"""data-testid="{Regex.Escape(testId)}"[^>]*>([^<]*)<""");
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value.Trim()) : null;
    }

    [GeneratedRegex("""<input[^>]*name="__RequestVerificationToken"[^>]*value="([^"]+)"[^>]*>""")]
    private static partial Regex AntiforgeryInput();

    [GeneratedRegex(@"CL-\d{5}")]
    private static partial Regex LoanNumber();

    [GeneratedRegex(@"href=""/Applications/\d+""")]
    private static partial Regex ApplicationLink();
}
