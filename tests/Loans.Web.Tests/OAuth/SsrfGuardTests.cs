using System.Net;
using System.Text;
using Loans.Web.OAuth;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Loans.Web.Tests.OAuth;

public class SsrfGuardTests
{
    private static readonly Uri Url = new("https://client.example/oauth/client.json");

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.255.0.1")]
    [InlineData("10.0.0.1")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("100.127.255.255")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("ff02::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("64:ff9b::a00:1")]
    [InlineData("2002:a00:1::")]
    [InlineData("2001:db8::1")]
    public void NonPublicAddresses_AreRejected(string address)
    {
        Assert.False(IpAddressClassifier.IsPubliclyRoutable(IPAddress.Parse(address)), address);
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("172.32.0.1")]
    [InlineData("100.128.0.1")]
    [InlineData("::ffff:8.8.8.8")]
    [InlineData("2606:4700:4700::1111")]
    public void PublicAddresses_AreAccepted(string address)
    {
        Assert.True(IpAddressClassifier.IsPubliclyRoutable(IPAddress.Parse(address)), address);
    }

    private sealed class FixedResolver(params string[] addresses) : IHostAddressResolver
    {
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
            Task.FromResult(addresses.Select(IPAddress.Parse).ToArray());
    }

    [Fact]
    public async Task Resolution_AnyPrivateAddress_RejectsHost()
    {
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            HttpClientMetadataFetcher.ResolveVettedAddressesAsync("client.example", new FixedResolver("8.8.8.8", "10.0.0.1"), default));
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            HttpClientMetadataFetcher.ResolveVettedAddressesAsync("client.example", new FixedResolver(), default));

        var vetted = await HttpClientMetadataFetcher.ResolveVettedAddressesAsync("client.example", new FixedResolver("8.8.8.8"), default);
        Assert.Single(vetted);
    }

    [Fact]
    public async Task RealHandler_ToPrivateHost_FailsWithoutConnecting()
    {
        using var fetcher = new HttpClientMetadataFetcher(
            HttpClientMetadataFetcher.CreateHandler(new FixedResolver("127.0.0.1")),
            Options());

        var result = await fetcher.FetchAsync(Url);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void RealHandler_DoesNotFollowRedirectsOrSendCredentials()
    {
        using var handler = HttpClientMetadataFetcher.CreateHandler(new FixedResolver("8.8.8.8"));

        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseCookies);
        Assert.False(handler.UseProxy);
        Assert.Null(handler.Credentials);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return respond(request, cancellationToken);
        }
    }

    private static IOptions<OAuthOptions> Options(int timeoutSeconds = 5, int maxBytes = 64) =>
        Microsoft.Extensions.Options.Options.Create(new OAuthOptions
        {
            Cimd = new CimdOptions { FetchTimeoutSeconds = timeoutSeconds, MaxDocumentBytes = maxBytes },
        });

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK, string mediaType = "application/json") =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

    [Fact]
    public async Task Fetch_Redirect_IsNotFollowed()
    {
        var stub = new StubHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri("https://evil.example/doc.json");
            return Task.FromResult(response);
        });
        using var fetcher = new HttpClientMetadataFetcher(stub, Options());

        var result = await fetcher.FetchAsync(Url);

        Assert.True(result.IsFailure);
        Assert.Equal(1, stub.Calls);
    }

    [Fact]
    public async Task Fetch_OversizedBody_IsRejected()
    {
        using var fetcher = new HttpClientMetadataFetcher(new StubHandler((_, _) => Task.FromResult(Json(new string('x', 65)))), Options(maxBytes: 64));

        Assert.True((await fetcher.FetchAsync(Url)).IsFailure);
    }

    [Fact]
    public async Task Fetch_OversizedBodyWithoutContentLength_IsRejected()
    {
        using var fetcher = new HttpClientMetadataFetcher(new StubHandler((_, _) =>
        {
            var content = new StreamContent(new MemoryStream(new byte[1000]));
            content.Headers.ContentType = new("application/json");
            content.Headers.ContentLength = null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }), Options(maxBytes: 64));

        Assert.True((await fetcher.FetchAsync(Url)).IsFailure);
    }

    [Fact]
    public async Task Fetch_NonJson_IsRejected()
    {
        using var fetcher = new HttpClientMetadataFetcher(new StubHandler((_, _) => Task.FromResult(Json("{}", mediaType: "text/html"))), Options());

        Assert.True((await fetcher.FetchAsync(Url)).IsFailure);
    }

    [Fact]
    public async Task Fetch_Timeout_IsHonored()
    {
        using var fetcher = new HttpClientMetadataFetcher(new StubHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Json("{}");
        }), Options(timeoutSeconds: 1));

        var result = await fetcher.FetchAsync(Url);

        Assert.True(result.IsFailure);
        Assert.Equal("Timed out.", result.Error.Message);
    }

    [Fact]
    public async Task Fetch_Success_UsesGetAndReturnsMaxAge()
    {
        HttpMethod? method = null;
        using var fetcher = new HttpClientMetadataFetcher(new StubHandler((request, _) =>
        {
            method = request.Method;
            var response = Json("{\"a\":1}", mediaType: "application/oauth-client+json");
            response.Headers.CacheControl = new() { MaxAge = TimeSpan.FromMinutes(30) };
            return Task.FromResult(response);
        }), Options());

        var result = await fetcher.FetchAsync(Url);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Get, method);
        Assert.Equal(TimeSpan.FromMinutes(30), result.Value.MaxAge);
    }

    private sealed class CountingFetcher(Func<int, Loans.Core.Common.Result<FetchedClientMetadata>> respond) : IClientMetadataFetcher
    {
        public int Calls { get; private set; }

        public Task<Loans.Core.Common.Result<FetchedClientMetadata>> FetchAsync(Uri url, CancellationToken cancellationToken = default) =>
            Task.FromResult(respond(++Calls));
    }

    private static readonly string ValidDoc = OAuthDriver.Document(Url.OriginalString);

    [Theory]
    [InlineData(null, 5)]           // no max-age → minimum
    [InlineData(10, 5)]             // below minimum → clamped up
    [InlineData(3600, 60)]          // honored
    [InlineData(999_999, 1440)]     // above maximum → clamped down
    public async Task Resolver_ClampsCacheLifetime(int? maxAgeSeconds, int expectedMinutes)
    {
        var fetcher = new CountingFetcher(_ => Loans.Core.Common.Result<FetchedClientMetadata>.Success(
            new FetchedClientMetadata(ValidDoc, maxAgeSeconds is { } s ? TimeSpan.FromSeconds(s) : null)));
        var clock = new AdjustableTimeProvider();
        using var resolver = new CimdClientResolver(fetcher, Options(), clock, NullLogger<CimdClientResolver>.Instance);

        await resolver.ResolveAsync(Url.OriginalString);
        clock.Advance(TimeSpan.FromMinutes(expectedMinutes) - TimeSpan.FromSeconds(5));
        await resolver.ResolveAsync(Url.OriginalString);
        Assert.Equal(1, fetcher.Calls);

        clock.Advance(TimeSpan.FromSeconds(10));
        await resolver.ResolveAsync(Url.OriginalString);
        Assert.Equal(2, fetcher.Calls);
    }

    [Fact]
    public async Task Resolver_NegativeCachesFailuresForOneMinute()
    {
        var fetcher = new CountingFetcher(_ => Loans.Core.Common.Result<FetchedClientMetadata>.Failure(new("cimd.fetch_failed", "nope")));
        var clock = new AdjustableTimeProvider();
        using var resolver = new CimdClientResolver(fetcher, Options(), clock, NullLogger<CimdClientResolver>.Instance);

        Assert.True((await resolver.ResolveAsync(Url.OriginalString)).IsFailure);
        Assert.True((await resolver.ResolveAsync(Url.OriginalString)).IsFailure);
        Assert.Equal(1, fetcher.Calls);

        clock.Advance(TimeSpan.FromSeconds(61));
        await resolver.ResolveAsync(Url.OriginalString);
        Assert.Equal(2, fetcher.Calls);
    }
}
