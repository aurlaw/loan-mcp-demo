namespace Loans.Web.OAuth;

public sealed class OAuthOptions
{
    public const string SectionName = "OAuth";

    public string Issuer { get; set; } = string.Empty;
    public string McpResource { get; set; } = string.Empty;
    public int AccessTokenLifetimeMinutes { get; set; } = 60;
    public int RefreshTokenLifetimeDays { get; set; } = 14;

    /// <summary>Root for persisted keys (<c>keys/</c>, <c>dataprotection/</c>); relative paths resolve against the content root.</summary>
    public string KeyStoragePath { get; set; } = "App_Data";

    public CimdOptions Cimd { get; set; } = new();
    public Dictionary<string, OAuthClientOptions> Clients { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CimdOptions
{
    public bool Enabled { get; set; } = true;
    public int FetchTimeoutSeconds { get; set; } = 5;
    public int MaxDocumentBytes { get; set; } = 16_384;
    public int MinCacheMinutes { get; set; } = 5;
    public int MaxCacheHours { get; set; } = 24;
}

public sealed class OAuthClientOptions
{
    public string? ClientId { get; set; }
    public string? DisplayName { get; set; }
    public string Type { get; set; } = "Confidential";
    public string? Secret { get; set; }
    public List<string> RedirectUris { get; set; } = [];
    public bool DevelopmentOnly { get; set; }
}
