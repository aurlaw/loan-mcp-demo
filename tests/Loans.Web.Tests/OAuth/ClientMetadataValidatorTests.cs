using System.Text.Json;
using Loans.Web.OAuth;

namespace Loans.Web.Tests.OAuth;

public class ClientMetadataValidatorTests
{
    private const string Url = "https://client.example/oauth/client.json";

    private static string Doc(object? redirects = null, string clientId = Url, string? method = null, string[]? grants = null, string[]? responses = null, string? name = null)
    {
        var d = new Dictionary<string, object?> { ["client_id"] = clientId, ["redirect_uris"] = redirects ?? new[] { "https://client.example/cb" } };
        if (method is not null) { d["token_endpoint_auth_method"] = method; }
        if (grants is not null) { d["grant_types"] = grants; }
        if (responses is not null) { d["response_types"] = responses; }
        if (name is not null) { d["client_name"] = name; }
        return JsonSerializer.Serialize(d);
    }

    [Theory]
    [InlineData("http://client.example/oauth.json")]
    [InlineData("https://client.example")]
    [InlineData("https://client.example/")]
    [InlineData("https://client.example?x=1")]
    [InlineData("https://client.example/a.json#frag")]
    [InlineData("https://user:pw@client.example/a.json")]
    [InlineData("https://user@client.example/a.json")]
    [InlineData("https://client.example/a/../b.json")]
    [InlineData("https://client.example/./b.json")]
    [InlineData("https://client.example/a/%2e%2E/b.json")]
    [InlineData("https://client.example/a b.json")]
    [InlineData("https://client.example\\@evil.example/a.json")]
    [InlineData("not-a-url")]
    [InlineData("")]
    public void ClientId_Invalid_IsRejected(string clientId)
    {
        Assert.True(ClientMetadataValidator.ValidateClientId(clientId).IsFailure, clientId);
    }

    [Fact]
    public void ClientId_TooLong_IsRejected()
    {
        Assert.True(ClientMetadataValidator.ValidateClientId("https://client.example/" + new string('a', 2048)).IsFailure);
    }

    [Theory]
    [InlineData(Url)]
    [InlineData("https://client.example:8443/a/b.json?v=1")]
    public void ClientId_Valid_IsAccepted(string clientId)
    {
        Assert.True(ClientMetadataValidator.ValidateClientId(clientId).IsSuccess);
    }

    [Fact]
    public void Document_Minimal_IsAccepted_WithHostAsNameFallback()
    {
        var result = ClientMetadataValidator.ValidateDocument(Url, Doc());

        Assert.True(result.IsSuccess);
        Assert.Equal("client.example", result.Value.DisplayName);
        Assert.Equal("client.example", result.Value.Host);
        Assert.Equal(["https://client.example/cb"], result.Value.RedirectUris);
    }

    [Fact]
    public void Document_UsesClientName_WithControlCharsRemoved()
    {
        var result = ClientMetadataValidator.ValidateDocument(Url, Doc(name: "  My\u0007 App "));

        Assert.Equal("My App", result.Value.DisplayName);
    }

    [Fact]
    public void Document_ClientIdMismatch_IsRejected()
    {
        Assert.True(ClientMetadataValidator.ValidateDocument(Url, Doc(clientId: Url + "x")).IsFailure);
        Assert.True(ClientMetadataValidator.ValidateDocument(Url, Doc(clientId: "https://CLIENT.example/oauth/client.json")).IsFailure);
    }

    public static TheoryData<string[]> BadRedirects() => new()
    {
        Array.Empty<string>(),
        new[] { "/relative" },
        new[] { "http://client.example/cb" },
        new[] { "https://client.example/cb#frag" },
        new[] { "https://user@client.example/cb" },
        new[] { "myapp://callback" },
        new[] { "https://client.example/ok", "http://evil.example/cb" },
    };

    [Theory]
    [MemberData(nameof(BadRedirects))]
    public void Document_BadRedirects_AreRejected(string[] redirects)
    {
        Assert.True(ClientMetadataValidator.ValidateDocument(Url, Doc(redirects)).IsFailure);
    }

    [Theory]
    [InlineData("http://localhost:3000/cb")]
    [InlineData("http://127.0.0.1/cb")]
    [InlineData("http://[::1]:8080/cb")]
    public void Document_HttpLoopbackRedirect_IsAccepted(string redirect)
    {
        Assert.True(ClientMetadataValidator.ValidateDocument(Url, Doc(new[] { redirect })).IsSuccess);
    }

    [Theory]
    [InlineData("private_key_jwt")]
    [InlineData("client_secret_basic")]
    [InlineData("client_secret_post")]
    public void Document_NonPublicAuthMethods_AreRejected(string method)
    {
        Assert.True(ClientMetadataValidator.ValidateDocument(Url, Doc(method: method)).IsFailure);
    }

    [Fact]
    public void Document_AuthMethodNone_IsAccepted()
    {
        Assert.True(ClientMetadataValidator.ValidateDocument(Url, Doc(method: "none")).IsSuccess);
    }

    [Fact]
    public void Document_GrantAndResponseTypes_AreEnforced()
    {
        Assert.True(ClientMetadataValidator.ValidateDocument(Url, Doc(responses: ["token"])).IsFailure);
        Assert.True(ClientMetadataValidator.ValidateDocument(Url, Doc(grants: ["refresh_token"])).IsFailure);
        Assert.True(ClientMetadataValidator.ValidateDocument(Url, Doc(grants: ["authorization_code", "refresh_token"], responses: ["code"])).IsSuccess);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void Document_Malformed_IsRejected(string json)
    {
        Assert.True(ClientMetadataValidator.ValidateDocument(Url, json).IsFailure);
    }
}
