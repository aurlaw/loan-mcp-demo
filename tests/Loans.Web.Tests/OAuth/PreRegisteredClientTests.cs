using System.Net;
using Loans.Core.Data.Seeding;
using Loans.Web.OAuth;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;

namespace Loans.Web.Tests.OAuth;

public class PreRegisteredClientTests
{
    private const string Secret = "test-client-secret-value";

    private static Dictionary<string, string> Client(string name, string clientId, string type, string? secret = null, bool developmentOnly = false)
    {
        var settings = new Dictionary<string, string>
        {
            [$"OAuth:Clients:{name}:ClientId"] = clientId,
            [$"OAuth:Clients:{name}:DisplayName"] = $"{name} client",
            [$"OAuth:Clients:{name}:Type"] = type,
            [$"OAuth:Clients:{name}:RedirectUris:0"] = OAuthDriver.RedirectUri,
            [$"OAuth:Clients:{name}:DevelopmentOnly"] = developmentOnly ? "true" : "false",
        };
        if (secret is not null)
        {
            settings[$"OAuth:Clients:{name}:Secret"] = secret;
        }

        return settings;
    }

    private static async Task<bool> ClientExistsAsync(OAuthWebFactory factory, string clientId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        return await applications.FindByClientIdAsync(clientId) is not null;
    }

    [Fact]
    public async Task Confidential_CompletesFlowWithSecret_BadSecretIsInvalidClient()
    {
        await using var factory = new OAuthWebFactory(Client("Tool", "tool-client", "Confidential", Secret));
        var driver = new OAuthDriver(factory);

        var code = await driver.GetCodeAsync(SeedUsers.Officer.Email, "tool-client");

        var bad = await driver.ExchangeCodeAsync("tool-client", code, "wrong-secret");
        Assert.Equal("invalid_client", bad.Error);

        var good = await driver.ExchangeCodeAsync("tool-client", code, Secret);
        Assert.Equal(HttpStatusCode.OK, good.Status);
        Assert.Equal([OAuthWebFactory.Resource], good.Jwt.Audiences);

        var refreshed = await driver.RefreshAsync("tool-client", good.RefreshToken!, Secret);
        Assert.Equal(HttpStatusCode.OK, refreshed.Status);
    }

    [Fact]
    public async Task Public_CompletesFlowWithPkceOnly()
    {
        await using var factory = new OAuthWebFactory(Client("Native", "native-client", "Public"));
        var driver = new OAuthDriver(factory);

        var tokens = await driver.CompleteFlowAsync(SeedUsers.Admin.Email, "native-client");

        Assert.Contains("loans.read", tokens.JwtScopes);
    }

    [Fact]
    public async Task Consent_ForPreRegisteredClient_HasNoMetadataHost()
    {
        await using var factory = new OAuthWebFactory(Client("Tool", "tool-client", "Public"));
        var driver = new OAuthDriver(factory);
        await driver.SignInAsync(SeedUsers.Officer.Email);

        var html = await driver.Browser.GetStringAsync(driver.AuthorizeUrl("tool-client"));

        Assert.Equal("Tool client", WebTestHelpers.TestIdText(html, "consent-client"));
        Assert.False(WebTestHelpers.HasTestId(html, "consent-host"));
    }

    [Fact]
    public async Task Confidential_WithoutSecret_FailsFast()
    {
        // Program logs this and exits with code 1; assert on the seeder it calls at startup.
        await using var factory = new OAuthWebFactory(Client("Tool", "tool-client", "Confidential"));
        await using var scope = factory.Services.CreateAsyncScope();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => scope.ServiceProvider.GetRequiredService<OAuthClientSeeder>().SeedAsync());

        Assert.Contains("OAuth:Clients:Tool:Secret", ex.Message);
    }

    [Fact]
    public async Task SecretOnlyEntry_IsSkipped_AndAppStarts()
    {
        await using var factory = new OAuthWebFactory(new Dictionary<string, string> { ["OAuth:Clients:Leftover:Secret"] = "ignored" });

        var response = await factory.CreateBrowser().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Seeding_IsIdempotent_AndUpdatesExistingClient()
    {
        await using var first = new OAuthWebFactory(Client("Tool", "tool-client", "Public"));
        Assert.True(await ClientExistsAsync(first, "tool-client"));

        var changed = Client("Tool", "tool-client", "Public");
        changed["OAuth:Clients:Tool:DisplayName"] = "Renamed tool";
        await using var second = new OAuthWebFactory(changed, shareStorageWith: first);

        await using var scope = second.Services.CreateAsyncScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var application = await applications.FindByClientIdAsync("tool-client");
        Assert.Equal("Renamed tool", await applications.GetDisplayNameAsync(application!));
        Assert.Equal(1, await applications.CountAsync());
    }

    [Fact]
    public async Task DevelopmentOnly_IsSeededInDevelopment_ButNotOutside()
    {
        await using var development = new OAuthWebFactory(Client("Dev", "dev-client", "Public", developmentOnly: true));
        Assert.True(await ClientExistsAsync(development, "dev-client"));

        var settings = Client("Dev", "dev-only-2", "Public", developmentOnly: true);
        foreach (var (key, value) in Client("Prod", "prod-client", "Public"))
        {
            settings[key] = value;
        }

        await using var production = new OAuthWebFactory(settings, "Production", shareStorageWith: development);

        Assert.False(await ClientExistsAsync(production, "dev-only-2"));
        Assert.True(await ClientExistsAsync(production, "prod-client"));
    }

    [Fact]
    public async Task UrlShapedPreRegisteredClientId_FailsFast()
    {
        await using var factory = new OAuthWebFactory(Client("Bad", "https://client.example/x.json", "Public"));
        await using var scope = factory.Services.CreateAsyncScope();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => scope.ServiceProvider.GetRequiredService<OAuthClientSeeder>().SeedAsync());

        Assert.Contains(nameof(OAuthClientOptions.ClientId), ex.Message);
    }
}
