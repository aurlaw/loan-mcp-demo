using System.Text.Json;
using Loans.Core.Common;

namespace Loans.Web.OAuth;

/// <summary>A validated Client ID Metadata Document.</summary>
public sealed record ClientMetadata(string ClientId, string DisplayName, string Host, IReadOnlyList<string> RedirectUris);

/// <summary>Pure validation of CIMD client ID URLs and documents.</summary>
public static class ClientMetadataValidator
{
    public const string InvalidClientIdCode = "cimd.invalid_client_id";
    public const string InvalidDocumentCode = "cimd.invalid_document";

    private const int MaxClientIdLength = 2048;
    private const int MaxRedirectUris = 20;
    private const int MaxClientNameLength = 100;

    private static readonly JsonDocumentOptions JsonOptions = new() { MaxDepth = 16 };

    /// <summary>Whether a client_id should be resolved through CIMD rather than looked up as a registered ID.</summary>
    public static bool IsUrlShaped(string? clientId) =>
        clientId is not null && clientId.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    public static Result<Uri> ValidateClientId(string? clientId)
    {
        if (string.IsNullOrEmpty(clientId) || clientId.Length > MaxClientIdLength)
        {
            return InvalidClientId("Client ID must be a URL of at most 2048 characters.");
        }

        if (!clientId.StartsWith("https://", StringComparison.Ordinal))
        {
            return InvalidClientId("Client ID must use https.");
        }

        if (clientId.Any(c => c <= 0x20 || c >= 0x7F || c == '\\'))
        {
            return InvalidClientId("Client ID contains invalid characters.");
        }

        if (clientId.Contains('#'))
        {
            return InvalidClientId("Client ID must not contain a fragment.");
        }

        if (!Uri.TryCreate(clientId, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
        {
            return InvalidClientId("Client ID must be an absolute URL.");
        }

        // Inspect the raw string: Uri normalizes dot segments away.
        var afterScheme = clientId["https://".Length..];
        var pathStart = afterScheme.IndexOf('/');
        var authorityEnd = pathStart >= 0 ? pathStart : afterScheme.IndexOf('?');
        var authority = authorityEnd >= 0 ? afterScheme[..authorityEnd] : afterScheme;
        if (authority.Contains('@') || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return InvalidClientId("Client ID must not contain user info.");
        }

        if (pathStart < 0)
        {
            return InvalidClientId("Client ID must have a path.");
        }

        var path = afterScheme[pathStart..];
        var queryStart = path.IndexOf('?');
        if (queryStart >= 0)
        {
            path = path[..queryStart];
        }

        if (path == "/")
        {
            return InvalidClientId("Client ID must have a non-root path.");
        }

        foreach (var segment in path.Split('/'))
        {
            var decoded = segment.Replace("%2e", ".", StringComparison.OrdinalIgnoreCase);
            if (decoded is "." or "..")
            {
                return InvalidClientId("Client ID must not contain dot segments.");
            }
        }

        return Result<Uri>.Success(uri);
    }

    public static Result<ClientMetadata> ValidateDocument(string clientId, string json)
    {
        var url = ValidateClientId(clientId);
        if (url.IsFailure)
        {
            return Result<ClientMetadata>.Failure(url.Error);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, JsonOptions);
        }
        catch (JsonException)
        {
            return InvalidDocument("Document is not valid JSON.");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return InvalidDocument("Document must be a JSON object.");
            }

            if (!root.TryGetProperty("client_id", out var documentClientId)
                || documentClientId.ValueKind != JsonValueKind.String
                || !string.Equals(documentClientId.GetString(), clientId, StringComparison.Ordinal))
            {
                return InvalidDocument("client_id must exactly match the document URL.");
            }

            if (root.TryGetProperty("token_endpoint_auth_method", out var authMethod)
                && (authMethod.ValueKind != JsonValueKind.String || authMethod.GetString() != "none"))
            {
                return InvalidDocument("Only public clients (token_endpoint_auth_method 'none') are supported.");
            }

            if (root.TryGetProperty("grant_types", out var grantTypes) && !ContainsString(grantTypes, "authorization_code"))
            {
                return InvalidDocument("grant_types must include authorization_code.");
            }

            if (root.TryGetProperty("response_types", out var responseTypes) && !ContainsString(responseTypes, "code"))
            {
                return InvalidDocument("response_types must include code.");
            }

            if (!root.TryGetProperty("redirect_uris", out var redirects)
                || redirects.ValueKind != JsonValueKind.Array
                || redirects.GetArrayLength() is 0 or > MaxRedirectUris)
            {
                return InvalidDocument("redirect_uris must be a non-empty array of at most 20 URIs.");
            }

            var redirectUris = new List<string>(redirects.GetArrayLength());
            foreach (var redirect in redirects.EnumerateArray())
            {
                if (redirect.ValueKind != JsonValueKind.String || !IsValidRedirectUri(redirect.GetString()!))
                {
                    return InvalidDocument("redirect_uris must be absolute https URIs (http only for loopback) without fragments.");
                }

                redirectUris.Add(redirect.GetString()!);
            }

            var host = url.Value.Host;
            var name = root.TryGetProperty("client_name", out var clientName) && clientName.ValueKind == JsonValueKind.String
                ? SanitizeName(clientName.GetString()!)
                : string.Empty;

            return Result<ClientMetadata>.Success(new ClientMetadata(
                clientId,
                name.Length > 0 ? name : host,
                host,
                redirectUris));
        }
    }

    public static bool IsLoopbackHost(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host is "127.0.0.1" or "[::1]";

    private static bool IsValidRedirectUri(string value)
    {
        if (value.Contains('#') || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttps
            || (uri.Scheme == Uri.UriSchemeHttp && IsLoopbackHost(uri.Host));
    }

    private static bool ContainsString(JsonElement element, string value) =>
        element.ValueKind == JsonValueKind.Array
        && element.EnumerateArray().Any(e => e.ValueKind == JsonValueKind.String && e.GetString() == value);

    private static string SanitizeName(string name)
    {
        var cleaned = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return cleaned.Length <= MaxClientNameLength ? cleaned : cleaned[..MaxClientNameLength].Trim();
    }

    private static Result<Uri> InvalidClientId(string message) =>
        Result<Uri>.Failure(new Error(InvalidClientIdCode, message));

    private static Result<ClientMetadata> InvalidDocument(string message) =>
        Result<ClientMetadata>.Failure(new Error(InvalidDocumentCode, message));
}
