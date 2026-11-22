using Loans.Core.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using OpenIddict.EntityFrameworkCore.Models;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Loans.Web.OAuth;

public static class OAuthServiceCollectionExtensions
{
    public static IServiceCollection AddLoansOAuth(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var section = configuration.GetSection(OAuthOptions.SectionName);
        var oauth = section.Get<OAuthOptions>() ?? new OAuthOptions();
        if (!Uri.TryCreate(oauth.Issuer, UriKind.Absolute, out var issuer))
        {
            throw new InvalidOperationException("OAuth:Issuer must be an absolute URL.");
        }

        if (!Uri.TryCreate(oauth.McpResource, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("OAuth:McpResource must be an absolute URL.");
        }

        services.Configure<OAuthOptions>(section);

        var keyRoot = Path.GetFullPath(oauth.KeyStoragePath, environment.ContentRootPath);
        var dataProtectionPath = Path.Combine(keyRoot, "dataprotection");
        PersistedRsaKeys.EnsurePrivateDirectory(keyRoot);
        PersistedRsaKeys.EnsurePrivateDirectory(dataProtectionPath);
        services.AddDataProtection()
            .SetApplicationName("Loans.Web")
            .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath));

        // Default KnownProxies/KnownNetworks are loopback only (cloudflared runs locally).
        services.Configure<ForwardedHeadersOptions>(options =>
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);

        services.AddSingleton<IHostAddressResolver, DnsHostAddressResolver>();
        services.AddSingleton<IClientMetadataFetcher>(sp => new HttpClientMetadataFetcher(
            HttpClientMetadataFetcher.CreateHandler(sp.GetRequiredService<IHostAddressResolver>()),
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OAuthOptions>>()));
        services.AddSingleton<ICimdClientResolver, CimdClientResolver>();
        services.AddSingleton<CimdMaterializationGate>();
        services.AddScoped<OAuthClientSeeder>();

        services.AddOpenIddict()
            .AddCore(options => options.ReplaceApplicationManager<OpenIddictEntityFrameworkCoreApplication, CimdApplicationManager>())
            .AddServer(options =>
            {
                options.SetIssuer(issuer)
                    .SetAuthorizationEndpointUris("connect/authorize")
                    .SetTokenEndpointUris("connect/token");

                options.AllowAuthorizationCodeFlow()
                    .RequireProofKeyForCodeExchange()
                    .AllowRefreshTokenFlow();

                // OpenIddict enables "plain" by default; this server is S256 only.
                options.Configure(o => o.CodeChallengeMethods.Remove(CodeChallengeMethods.Plain));

                options.RegisterScopes([.. OAuthScopePolicy.IdentityScopes, .. CapabilityScopes.All]);
                options.RegisterResources(oauth.McpResource);

                options.AddSigningKey(PersistedRsaKeys.LoadOrCreate(Path.Combine(keyRoot, "keys", "signing.pem")));
                options.AddEncryptionKey(PersistedRsaKeys.LoadOrCreate(Path.Combine(keyRoot, "keys", "encryption.pem")));
                options.DisableAccessTokenEncryption();

                options.SetAccessTokenLifetime(TimeSpan.FromMinutes(oauth.AccessTokenLifetimeMinutes));
                options.SetRefreshTokenLifetime(TimeSpan.FromDays(oauth.RefreshTokenLifetimeDays));

                options.AddEventHandler(DiscoveryMetadataHandler.Descriptor);

                var aspNetCore = options.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough();
                if (environment.IsDevelopment())
                {
                    aspNetCore.DisableTransportSecurityRequirement();
                }
            })
            .AddValidation(options =>
            {
                // Registered for Phase 5; not attached to any endpoint yet.
                options.UseLocalServer();
                options.AddAudiences(oauth.McpResource);
                options.UseAspNetCore();
            });

        return services;
    }
}
