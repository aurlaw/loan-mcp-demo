using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Loans.Core.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Loans.Web.Tests.OAuth;

public sealed record TokenResponse(HttpStatusCode Status, JsonElement Json)
{
    public string? AccessToken => Get("access_token");
    public string? RefreshToken => Get("refresh_token");
    public string? Error => Get("error");

    public JsonWebToken Jwt => new(AccessToken!);

    public IReadOnlySet<string> JwtScopes => Jwt.GetPayloadValue<string>("scope").Split(' ').ToHashSet();

    private string? Get(string name) => Json.TryGetProperty(name, out var value) ? value.GetString() : null;
}

/// <summary>Drives a browser + client through the authorization code flow.</summary>
public sealed partial class OAuthDriver(OAuthWebFactory factory)
{
    public const string RedirectUri = "https://client.example/callback";

    public HttpClient Browser { get; } = factory.CreateBrowser();
    public string Verifier { get; } = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
    public string Challenge => Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(Verifier)));

    public static string NewClientUrl() => $"https://client.example/oauth/{Guid.NewGuid():N}.json";

    public static string Document(string clientId, string? name = "Test Assistant", params string[] redirectUris)
    {
        var uris = redirectUris.Length == 0 ? [RedirectUri] : redirectUris;
        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["client_id"] = clientId,
            ["client_name"] = name,
            ["redirect_uris"] = uris,
            ["grant_types"] = new[] { "authorization_code", "refresh_token" },
            ["response_types"] = new[] { "code" },
            ["token_endpoint_auth_method"] = "none",
        });
    }

    public string RegisterCimdClient(string? name = "Test Assistant")
    {
        var url = NewClientUrl();
        factory.Fetcher.Set(url, Document(url, name));
        return url;
    }

    public string AuthorizeUrl(
        string clientId,
        string redirectUri = RedirectUri,
        string scope = "openid offline_access loans.read underwriting.run decisions.write",
        string? resource = OAuthWebFactory.Resource,
        bool pkce = true,
        string challengeMethod = "S256",
        string? prompt = null)
    {
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = scope,
            ["state"] = "test-state",
        };
        if (resource is not null)
        {
            query["resource"] = resource;
        }

        if (pkce)
        {
            query["code_challenge"] = challengeMethod == "S256" ? Challenge : Verifier;
            query["code_challenge_method"] = challengeMethod;
        }

        if (prompt is not null)
        {
            query["prompt"] = prompt;
        }

        return QueryHelpers.AddQueryString("/connect/authorize", query);
    }

    public async Task SignInAsync(string email)
    {
        var response = await Browser.PostLoginAsync(email, OAuthWebFactory.Password);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    /// <summary>Submits the consent form on the given page.</summary>
    public async Task<HttpResponseMessage> SubmitConsentAsync(string consentHtml, string decision)
    {
        var fields = HiddenFields(consentHtml);
        fields.Add(new("consent", decision));
        return await Browser.PostAsync("/connect/authorize", new FormUrlEncodedContent(fields));
    }

    /// <summary>Signs in, allows consent and returns the authorization code.</summary>
    public async Task<string> GetCodeAsync(string email, string clientId, string? scope = null)
    {
        await SignInAsync(email);
        var url = scope is null ? AuthorizeUrl(clientId) : AuthorizeUrl(clientId, scope: scope);
        var consent = await Browser.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, consent.StatusCode);

        var redirect = await SubmitConsentAsync(await consent.Content.ReadAsStringAsync(), "allow");
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        var parameters = RedirectParameters(redirect);
        Assert.True(parameters.ContainsKey("code"), redirect.Headers.Location?.ToString());
        return parameters["code"]!;
    }

    public Task<TokenResponse> ExchangeCodeAsync(string clientId, string code, string? secret = null) =>
        TokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = RedirectUri,
            ["client_id"] = clientId,
            ["code_verifier"] = Verifier,
            ["resource"] = OAuthWebFactory.Resource,
        }, secret);

    public Task<TokenResponse> RefreshAsync(string clientId, string refreshToken, string? secret = null) =>
        TokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId,
            ["resource"] = OAuthWebFactory.Resource,
        }, secret);

    public async Task<TokenResponse> CompleteFlowAsync(string email, string clientId, string? scope = null)
    {
        var code = await GetCodeAsync(email, clientId, scope);
        var tokens = await ExchangeCodeAsync(clientId, code);
        Assert.Equal(HttpStatusCode.OK, tokens.Status);
        return tokens;
    }

    public async Task SetRoleAsync(string email, string role)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByEmailAsync(email))!;
        await users.RemoveFromRolesAsync(user, await users.GetRolesAsync(user));
        await users.AddToRoleAsync(user, role);
    }

    public static Dictionary<string, string?> RedirectParameters(HttpResponseMessage response)
    {
        var location = response.Headers.Location!;
        return QueryHelpers.ParseQuery(location.Query).ToDictionary(p => p.Key, p => (string?)p.Value.ToString());
    }

    /// <summary>The OAuth error from a redirect, or from OpenIddict's local "error:..." response body.</summary>
    public static async Task<string?> ErrorOfAsync(HttpResponseMessage response)
    {
        if (response.Headers.Location is not null)
        {
            return RedirectParameters(response).GetValueOrDefault("error");
        }

        var body = await response.Content.ReadAsStringAsync();
        return body.Split('\n').FirstOrDefault(l => l.StartsWith("error:", StringComparison.Ordinal))?["error:".Length..].Trim();
    }

    /// <summary>Hidden fields of the consent form only (the layout's sign-out form has its own antiforgery token).</summary>
    public static List<KeyValuePair<string, string>> HiddenFields(string html)
    {
        var start = html.IndexOf("data-testid=\"consent\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "Consent form not found.");
        var form = html[start..];
        form = form[..form.IndexOf("</form>", StringComparison.Ordinal)];

        var fields = new List<KeyValuePair<string, string>>();
        foreach (Match input in HiddenInput().Matches(form))
        {
            var name = NameAttribute().Match(input.Value).Groups[1].Value;
            var value = ValueAttribute().Match(input.Value).Groups[1].Value;
            fields.Add(new(WebUtility.HtmlDecode(name), WebUtility.HtmlDecode(value)));
        }

        return fields;
    }

    private async Task<TokenResponse> TokenAsync(Dictionary<string, string> form, string? secret)
    {
        if (secret is not null)
        {
            form["client_secret"] = secret;
        }

        // A separate cookie-less client: the token endpoint is called server-to-server.
        using var client = factory.CreateBrowser();
        var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(form));
        var body = await response.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrEmpty(body), $"Empty token response ({(int)response.StatusCode})");
        return new TokenResponse(response.StatusCode, JsonDocument.Parse(body).RootElement.Clone());
    }

    [GeneratedRegex("""<input[^>]*type="hidden"[^>]*>""")]
    private static partial Regex HiddenInput();

    [GeneratedRegex("""name="([^"]*)"\s*""")]
    private static partial Regex NameAttribute();

    [GeneratedRegex("""value="([^"]*)"\s*""")]
    private static partial Regex ValueAttribute();
}
