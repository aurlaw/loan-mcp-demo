using System.Net;
using System.Text.Json;
using Loans.Core.Data.Seeding;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Loans.Web.Tests.OAuth;

public sealed class OAuthFlowFixture : IAsyncDisposable
{
    public OAuthWebFactory Factory { get; } = new();

    public ValueTask DisposeAsync() => Factory.DisposeAsync();
}

public class OAuthFlowTests(OAuthFlowFixture fixture) : IClassFixture<OAuthFlowFixture>
{
    private readonly OAuthWebFactory factory = fixture.Factory;

    private OAuthDriver Driver() => new(factory);

    private async Task<JsonElement> DiscoveryAsync(string path = "/.well-known/openid-configuration")
    {
        var response = await factory.CreateBrowser().GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static string[] Strings(JsonElement doc, string name) =>
        doc.GetProperty(name).EnumerateArray().Select(e => e.GetString()!).ToArray();

    [Theory]
    [InlineData("/.well-known/openid-configuration")]
    [InlineData("/.well-known/oauth-authorization-server")]
    public async Task Discovery_AdvertisesRequiredMetadata(string path)
    {
        var doc = await DiscoveryAsync(path);

        Assert.Equal(OAuthWebFactory.Issuer, doc.GetProperty("issuer").GetString());
        Assert.StartsWith(OAuthWebFactory.Issuer, doc.GetProperty("authorization_endpoint").GetString());
        Assert.StartsWith(OAuthWebFactory.Issuer, doc.GetProperty("token_endpoint").GetString());
        Assert.StartsWith(OAuthWebFactory.Issuer, doc.GetProperty("jwks_uri").GetString());
        Assert.Equal(["S256"], Strings(doc, "code_challenge_methods_supported"));
        Assert.Contains("loans.read", Strings(doc, "scopes_supported"));
        Assert.Contains("underwriting.run", Strings(doc, "scopes_supported"));
        Assert.Contains("decisions.write", Strings(doc, "scopes_supported"));
        Assert.True(doc.GetProperty("client_id_metadata_document_supported").GetBoolean());
        Assert.True(doc.GetProperty("authorization_response_iss_parameter_supported").GetBoolean());
        Assert.Equal(
            ["client_secret_basic", "client_secret_post", "none"],
            Strings(doc, "token_endpoint_auth_methods_supported").Order());
        Assert.False(doc.TryGetProperty("registration_endpoint", out _));
        Assert.DoesNotContain("password", Strings(doc, "grant_types_supported"));
        Assert.DoesNotContain("client_credentials", Strings(doc, "grant_types_supported"));
    }

    [Fact]
    public async Task Discovery_IgnoresRequestHost_AndForwardedHeaders()
    {
        var client = factory.CreateBrowser();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/.well-known/openid-configuration");
        request.Headers.Host = "evil.example:8443";
        request.Headers.Add("X-Forwarded-Host", "evil.example");

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body).RootElement;

        Assert.DoesNotContain("evil.example", body);
        Assert.Equal(OAuthWebFactory.Issuer, doc.GetProperty("issuer").GetString());
        Assert.Equal(OAuthWebFactory.Issuer + "connect/authorize", doc.GetProperty("authorization_endpoint").GetString());
        Assert.Equal(OAuthWebFactory.Issuer + "connect/token", doc.GetProperty("token_endpoint").GetString());
        Assert.Equal(OAuthWebFactory.Issuer + ".well-known/jwks", doc.GetProperty("jwks_uri").GetString());
    }

    [Theory]
    [InlineData("https://client.example/oauth/does-not-exist.json")]
    [InlineData("unknown-client")]
    [InlineData("https://client.example/")]
    public async Task UnknownClient_IsRefusedAtBothEndpoints(string clientId)
    {
        var driver = Driver();

        // RFC 6749 §4.1.2.1: the authorization endpoint reports this locally (no redirect) as invalid_request.
        var authorize = await driver.Browser.GetAsync(driver.AuthorizeUrl(clientId));
        Assert.Equal(HttpStatusCode.BadRequest, authorize.StatusCode);
        Assert.Null(authorize.Headers.Location);
        Assert.Equal("invalid_request", await OAuthDriver.ErrorOfAsync(authorize));
        Assert.Contains("'client_id' is invalid", await authorize.Content.ReadAsStringAsync());

        var token = await driver.ExchangeCodeAsync(clientId, "any-code");
        Assert.Equal("invalid_client", token.Error);
    }

    [Fact]
    public async Task CimdHappyPath_LoginConsentCodeAndTokens()
    {
        var driver = Driver();
        var clientId = driver.RegisterCimdClient("Test Assistant");
        var authorizeUrl = driver.AuthorizeUrl(clientId);

        // Unauthenticated → login with the full authorize URL as ReturnUrl.
        var challenge = await driver.Browser.GetAsync(authorizeUrl);
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        Assert.Contains("/Account/Login", challenge.Headers.Location!.OriginalString);
        var returnUrl = OAuthDriver.RedirectParameters(challenge)["ReturnUrl"]!;
        Assert.Equal(
            Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri("http://x" + authorizeUrl).Query).OrderBy(p => p.Key).Select(p => (p.Key, p.Value.ToString())),
            Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri("http://x" + returnUrl).Query).OrderBy(p => p.Key).Select(p => (p.Key, p.Value.ToString())));

        // Sign in with that ReturnUrl → back to authorize.
        var login = await driver.Browser.PostLoginAsync(SeedUsers.Officer.Email, OAuthWebFactory.Password, returnUrl);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.StartsWith("/connect/authorize?", login.Headers.Location!.OriginalString);

        // Consent shows client, host, user and only the officer's capabilities.
        var consent = await driver.Browser.GetAsync(login.Headers.Location);
        var html = await consent.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, consent.StatusCode);
        Assert.Equal("Test Assistant", WebTestHelpers.TestIdText(html, "consent-client"));
        Assert.Equal("client.example", WebTestHelpers.TestIdText(html, "consent-host"));
        Assert.Equal(SeedUsers.Officer.DisplayName, WebTestHelpers.TestIdText(html, "consent-user"));
        Assert.Contains("View loans and applications", html);
        Assert.Contains("Run underwriting analysis", html);
        Assert.DoesNotContain("Record credit decisions", html);
        Assert.Equal("DENY", consent.Headers.GetValues("X-Frame-Options").Single());

        // Allow → code with iss.
        var redirect = await driver.SubmitConsentAsync(html, "allow");
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        Assert.StartsWith(OAuthDriver.RedirectUri, redirect.Headers.Location!.OriginalString);
        var parameters = OAuthDriver.RedirectParameters(redirect);
        Assert.Equal(OAuthWebFactory.Issuer, parameters["iss"]);
        Assert.Equal("test-state", parameters["state"]);

        // Exchange with PKCE, no secret.
        var tokens = await driver.ExchangeCodeAsync(clientId, parameters["code"]!);
        Assert.Equal(HttpStatusCode.OK, tokens.Status);
        Assert.NotNull(tokens.AccessToken);
        Assert.NotNull(tokens.RefreshToken);

        var jwt = tokens.Jwt;
        Assert.Equal([OAuthWebFactory.Resource], jwt.Audiences);
        Assert.Equal(OAuthWebFactory.Issuer, jwt.Issuer);
        Assert.False(string.IsNullOrEmpty(jwt.Subject));
        Assert.DoesNotContain(jwt.Claims, c => c.Type == "email");
    }

    [Theory]
    [InlineData("officer@loans.test", "loans.read underwriting.run offline_access")]
    [InlineData("underwriter@loans.test", "loans.read underwriting.run decisions.write offline_access")]
    [InlineData("admin@loans.test", "loans.read offline_access")]
    public async Task AccessToken_ScopesMatchRole(string email, string expected)
    {
        var driver = Driver();
        var clientId = driver.RegisterCimdClient();

        var tokens = await driver.CompleteFlowAsync(email, clientId, "loans.read underwriting.run decisions.write offline_access");

        Assert.Equal(expected.Split(' ').ToHashSet(), tokens.JwtScopes);
    }

    [Fact]
    public async Task NoCapabilityScopesRequested_GrantsAllRoleScopes_AndOfflineAccess()
    {
        var driver = Driver();
        var clientId = driver.RegisterCimdClient();

        var tokens = await driver.CompleteFlowAsync(SeedUsers.Officer.Email, clientId, "openid");

        Assert.Equal(new HashSet<string> { "openid", "offline_access", "loans.read", "underwriting.run" }, tokens.JwtScopes);
        Assert.NotNull(tokens.RefreshToken);
    }

    [Fact]
    public async Task RequestedCapabilityOutsideRole_IsAccessDenied()
    {
        var driver = Driver();
        var clientId = driver.RegisterCimdClient();
        await driver.SignInAsync(SeedUsers.Admin.Email);

        var response = await driver.Browser.GetAsync(driver.AuthorizeUrl(clientId, scope: "decisions.write"));

        Assert.Equal("access_denied", await OAuthDriver.ErrorOfAsync(response));
    }

    [Fact]
    public async Task RedirectUriNotInDocument_IsRejectedWithoutRedirect()
    {
        var driver = Driver();
        var clientId = driver.RegisterCimdClient();

        var response = await driver.Browser.GetAsync(driver.AuthorizeUrl(clientId, redirectUri: "https://evil.example/callback"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task DocumentChange_IsPickedUpAfterCacheExpiry()
    {
        await using var isolated = new OAuthWebFactory();
        var driver = new OAuthDriver(isolated);
        var clientId = OAuthDriver.NewClientUrl();
        const string newRedirect = "https://client.example/new-callback";
        isolated.Fetcher.Set(clientId, OAuthDriver.Document(clientId));
        await driver.SignInAsync(SeedUsers.Officer.Email);

        Assert.Equal(HttpStatusCode.OK, (await driver.Browser.GetAsync(driver.AuthorizeUrl(clientId))).StatusCode);

        isolated.Fetcher.Set(clientId, OAuthDriver.Document(clientId, "Renamed", newRedirect));
        Assert.Equal(HttpStatusCode.BadRequest, (await driver.Browser.GetAsync(driver.AuthorizeUrl(clientId, redirectUri: newRedirect))).StatusCode);

        isolated.Clock.Advance(TimeSpan.FromMinutes(6));

        var updated = await driver.Browser.GetAsync(driver.AuthorizeUrl(clientId, redirectUri: newRedirect));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("Renamed", WebTestHelpers.TestIdText(await updated.Content.ReadAsStringAsync(), "consent-client"));
        Assert.Equal(HttpStatusCode.BadRequest, (await driver.Browser.GetAsync(driver.AuthorizeUrl(clientId))).StatusCode);
    }

    [Theory]
    [InlineData("http://localhost:3000/cb", "http://localhost:49152/cb", true)]
    [InlineData("http://127.0.0.1/cb", "http://127.0.0.1:8123/cb", true)]
    [InlineData("http://localhost:3000/cb", "http://localhost:49152/other", false)]
    [InlineData("http://localhost:3000/cb", "http://127.0.0.1:3000/cb", false)]
    public async Task LoopbackRedirects_MatchOnAnyPort(string registered, string requested, bool allowed)
    {
        var driver = Driver();
        var clientId = OAuthDriver.NewClientUrl();
        factory.Fetcher.Set(clientId, OAuthDriver.Document(clientId, "Native", registered));
        await driver.SignInAsync(SeedUsers.Officer.Email);

        var response = await driver.Browser.GetAsync(driver.AuthorizeUrl(clientId, redirectUri: requested));

        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Consent_IsRememberedOnSecondAuthorize()
    {
        var driver = Driver();
        var clientId = driver.RegisterCimdClient();
        await driver.GetCodeAsync(SeedUsers.Officer.Email, clientId);

        var second = await driver.Browser.GetAsync(driver.AuthorizeUrl(clientId));

        Assert.Equal(HttpStatusCode.Redirect, second.StatusCode);
        Assert.True(OAuthDriver.RedirectParameters(second).ContainsKey("code"));
    }

    [Fact]
    public async Task Consent_Deny_ReturnsAccessDeniedWithIss()
    {
        var driver = Driver();
        var clientId = driver.RegisterCimdClient();
        await driver.SignInAsync(SeedUsers.Officer.Email);
        var consent = await driver.Browser.GetStringAsync(driver.AuthorizeUrl(clientId));

        var response = await driver.SubmitConsentAsync(consent, "deny");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var parameters = OAuthDriver.RedirectParameters(response);
        Assert.Equal("access_denied", parameters["error"]);
        Assert.Equal(OAuthWebFactory.Issuer, parameters["iss"]);
        Assert.False(parameters.ContainsKey("code"));
    }

    [Fact]
    public async Task Consent_PostWithoutAntiforgery_IsRejected()
    {
        var driver = Driver();
        var clientId = driver.RegisterCimdClient();
        await driver.SignInAsync(SeedUsers.Officer.Email);
        var consent = await driver.Browser.GetStringAsync(driver.AuthorizeUrl(clientId));
        var fields = OAuthDriver.HiddenFields(consent).Where(f => f.Key != "__RequestVerificationToken").ToList();
        fields.Add(new("consent", "allow"));

        var response = await driver.Browser.PostAsync("/connect/authorize", new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Borrower_IsRefusedBeforeConsent()
    {
        var driver = Driver();
        var clientId = driver.RegisterCimdClient();
        await driver.SignInAsync(SeedUsers.Borrower.Email);

        var response = await driver.Browser.GetAsync(driver.AuthorizeUrl(clientId));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var parameters = OAuthDriver.RedirectParameters(response);
        Assert.Equal("access_denied", parameters["error"]);
        Assert.Equal("This account can't be used with AI assistants.", parameters["error_description"]);
        Assert.Equal(OAuthWebFactory.Issuer, parameters["iss"]);
    }

    [Fact]
    public async Task MissingPkce_IsRejected()
    {
        var driver = Driver();
        var clientId = driver.RegisterCimdClient();

        var response = await driver.Browser.GetAsync(driver.AuthorizeUrl(clientId, pkce: false));

        Assert.Equal("invalid_request", await OAuthDriver.ErrorOfAsync(response));
    }

    [Fact]
    public async Task PlainPkce_IsRejected()
    {
        var driver = Driver();
        var clientId = driver.RegisterCimdClient();

        var response = await driver.Browser.GetAsync(driver.AuthorizeUrl(clientId, challengeMethod: "plain"));

        Assert.Equal("invalid_request", await OAuthDriver.ErrorOfAsync(response));
    }

    [Fact]
    public async Task WrongPkceVerifier_IsRejected()
    {
        var driver = Driver();
        var clientId = driver.RegisterCimdClient();
        var code = await driver.GetCodeAsync(SeedUsers.Officer.Email, clientId);

        var other = Driver();
        var tokens = await other.ExchangeCodeAsync(clientId, code);

        Assert.Equal("invalid_grant", tokens.Error);
    }

    [Fact]
    public async Task WrongResource_IsInvalidTarget()
    {
        var driver = Driver();
        var clientId = driver.RegisterCimdClient();

        var response = await driver.Browser.GetAsync(driver.AuthorizeUrl(clientId, resource: "https://other.example/api"));

        Assert.Equal("invalid_target", await OAuthDriver.ErrorOfAsync(response));
    }

    [Fact]
    public async Task Refresh_ReturnsNewTokens()
    {
        var driver = Driver();
        var clientId = driver.RegisterCimdClient();
        var tokens = await driver.CompleteFlowAsync(SeedUsers.Underwriter.Email, clientId);

        var refreshed = await driver.RefreshAsync(clientId, tokens.RefreshToken!);

        Assert.Equal(HttpStatusCode.OK, refreshed.Status);
        Assert.NotEqual(tokens.RefreshToken, refreshed.RefreshToken);
        Assert.Equal([OAuthWebFactory.Resource], refreshed.Jwt.Audiences);
        Assert.Contains("decisions.write", refreshed.JwtScopes);
    }

    [Fact]
    public async Task Refresh_RoleDowngradeToAdmin_NarrowsScopes_AndNeverWidens()
    {
        await using var isolated = new OAuthWebFactory();
        var driver = new OAuthDriver(isolated);
        var clientId = driver.RegisterCimdClient();
        var tokens = await driver.CompleteFlowAsync(SeedUsers.Underwriter.Email, clientId);

        await driver.SetRoleAsync(SeedUsers.Underwriter.Email, SeedRoles.Admin);
        var narrowed = await driver.RefreshAsync(clientId, tokens.RefreshToken!);
        Assert.Equal(HttpStatusCode.OK, narrowed.Status);
        Assert.Contains("loans.read", narrowed.JwtScopes);
        Assert.DoesNotContain("underwriting.run", narrowed.JwtScopes);
        Assert.DoesNotContain("decisions.write", narrowed.JwtScopes);

        await driver.SetRoleAsync(SeedUsers.Underwriter.Email, SeedRoles.Underwriter);
        var restored = await driver.RefreshAsync(clientId, narrowed.RefreshToken!);
        Assert.Equal(HttpStatusCode.OK, restored.Status);
        Assert.DoesNotContain("decisions.write", restored.JwtScopes);
    }

    [Fact]
    public async Task Refresh_RoleDowngradeToBorrower_IsInvalidGrant_AndRevokes()
    {
        await using var isolated = new OAuthWebFactory();
        var driver = new OAuthDriver(isolated);
        var clientId = driver.RegisterCimdClient();
        var tokens = await driver.CompleteFlowAsync(SeedUsers.Officer.Email, clientId);

        await driver.SetRoleAsync(SeedUsers.Officer.Email, SeedRoles.Borrower);
        var refused = await driver.RefreshAsync(clientId, tokens.RefreshToken!);
        Assert.Equal("invalid_grant", refused.Error);

        await driver.SetRoleAsync(SeedUsers.Officer.Email, SeedRoles.LoanOfficer);
        var afterRestore = await driver.RefreshAsync(clientId, tokens.RefreshToken!);
        Assert.Equal("invalid_grant", afterRestore.Error);
    }

    [Fact]
    public async Task TokenEndpoint_WithoutCookies_IsNotRedirectedToLogin()
    {
        var response = await factory.CreateBrowser().PostAsync("/connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["grant_type"] = "authorization_code", ["client_id"] = "unknown", ["code"] = "x" }));

        Assert.NotEqual(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Keys_ArePersisted_SecondHostValidatesFirstHostsTokens()
    {
        await using var first = new OAuthWebFactory();
        var driver = new OAuthDriver(first);
        var clientId = driver.RegisterCimdClient();
        var tokens = await driver.CompleteFlowAsync(SeedUsers.Officer.Email, clientId);

        await using var second = new OAuthWebFactory(new Dictionary<string, string>(), shareStorageWith: first);
        var jwks = new JsonWebKeySet(await second.CreateBrowser().GetStringAsync("/.well-known/jwks"));

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(tokens.AccessToken!, new TokenValidationParameters
        {
            ValidIssuer = OAuthWebFactory.Issuer,
            ValidAudience = OAuthWebFactory.Resource,
            IssuerSigningKeys = jwks.GetSigningKeys(),
            ValidTypes = ["at+jwt"],
        });

        Assert.True(result.IsValid, result.Exception?.Message);
    }
}
