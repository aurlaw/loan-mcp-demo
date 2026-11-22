using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using Loans.Core.Common;
using Microsoft.Extensions.Options;

namespace Loans.Web.OAuth;

public sealed record FetchedClientMetadata(string Json, TimeSpan? MaxAge);

/// <summary>Fetches a Client ID Metadata Document (swappable in tests).</summary>
public interface IClientMetadataFetcher
{
    Task<Result<FetchedClientMetadata>> FetchAsync(Uri url, CancellationToken cancellationToken = default);
}

public interface IHostAddressResolver
{
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken);
}

public sealed class DnsHostAddressResolver : IHostAddressResolver
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
        Dns.GetHostAddressesAsync(host, cancellationToken);
}

/// <summary>
/// SSRF-safe fetch: vets every resolved address and connects to a vetted address (no DNS rebinding),
/// follows no redirects, sends no credentials, and enforces timeout, size and content type.
/// </summary>
public sealed class HttpClientMetadataFetcher : IClientMetadataFetcher, IDisposable
{
    public const string FetchFailedCode = "cimd.fetch_failed";

    private readonly HttpClient _http;
    private readonly CimdOptions _options;

    public HttpClientMetadataFetcher(HttpMessageHandler handler, IOptions<OAuthOptions> options)
    {
        _options = options.Value.Cimd;
        _http = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public static SocketsHttpHandler CreateHandler(IHostAddressResolver resolver) => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = false,
        Credentials = null,
        AutomaticDecompression = DecompressionMethods.None,
        MaxResponseHeadersLength = 16,
        PooledConnectionLifetime = TimeSpan.FromMinutes(1),
        ConnectCallback = (context, cancellationToken) =>
            ConnectToVettedAddressAsync(context.DnsEndPoint, resolver, cancellationToken),
    };

    /// <summary>Resolves a host; fails if it has no addresses or if <em>any</em> address is not publicly routable.</summary>
    public static async Task<IPAddress[]> ResolveVettedAddressesAsync(string host, IHostAddressResolver resolver, CancellationToken cancellationToken)
    {
        var addresses = await resolver.ResolveAsync(host, cancellationToken);
        if (addresses.Length == 0 || !addresses.All(IpAddressClassifier.IsPubliclyRoutable))
        {
            throw new HttpRequestException("Metadata host does not resolve exclusively to public addresses.");
        }

        return addresses;
    }

    public async Task<Result<FetchedClientMetadata>> FetchAsync(Uri url, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.FetchTimeoutSeconds));

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.UserAgent.ParseAdd("LoansMcpDemo-CIMD/1.0");

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                return Failed($"Unexpected status {(int)response.StatusCode}.");
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is null
                || !(mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
                     || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase)))
            {
                return Failed("Response is not JSON.");
            }

            if (response.Content.Headers.ContentLength > _options.MaxDocumentBytes)
            {
                return Failed("Document is too large.");
            }

            await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
            var buffer = new byte[_options.MaxDocumentBytes + 1];
            var total = 0;
            int read;
            while (total < buffer.Length && (read = await body.ReadAsync(buffer.AsMemory(total), timeout.Token)) > 0)
            {
                total += read;
            }

            if (total > _options.MaxDocumentBytes)
            {
                return Failed("Document is too large.");
            }

            return Result<FetchedClientMetadata>.Success(new FetchedClientMetadata(
                Encoding.UTF8.GetString(buffer, 0, total),
                response.Headers.CacheControl?.MaxAge));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed("Timed out.");
        }
        catch (HttpRequestException)
        {
            return Failed("Request failed.");
        }
        catch (IOException)
        {
            return Failed("Request failed.");
        }
    }

    public void Dispose() => _http.Dispose();

    private static async ValueTask<Stream> ConnectToVettedAddressAsync(
        DnsEndPoint endpoint,
        IHostAddressResolver resolver,
        CancellationToken cancellationToken)
    {
        var addresses = await ResolveVettedAddressesAsync(endpoint.Host, resolver, cancellationToken);

        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, endpoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException)
            {
                socket.Dispose();
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        throw new HttpRequestException("Could not connect to metadata host.");
    }

    private static Result<FetchedClientMetadata> Failed(string message) =>
        Result<FetchedClientMetadata>.Failure(new Error(FetchFailedCode, message));
}
