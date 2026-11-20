using Loans.Core.Data;
using Loans.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddSerilog((services, logger) =>
    {
        logger
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext();

        var otlpEndpoint = builder.Configuration["Otlp:Endpoint"];
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            logger.WriteTo.OpenTelemetry(options =>
            {
                options.Endpoint = otlpEndpoint;
                options.ResourceAttributes = new Dictionary<string, object>
                {
                    ["service.name"] = builder.Environment.ApplicationName,
                };
            });
        }
    });

    // Core registers Identity stores; the host adds sign-in and the display-name claim on the same builder.
    builder.Services.AddLoansCore(
        builder.Configuration,
        builder.Environment.ContentRootPath,
        identity => identity
            .AddSignInManager()
            .AddClaimsPrincipalFactory<DisplayNameClaimsPrincipalFactory>());

    // Identity cookie is the default scheme for pages. /mcp (Phase 5) will use its own bearer-only policy.
    builder.Services
        .AddAuthentication(IdentityConstants.ApplicationScheme)
        .AddIdentityCookies();

    builder.Services.ConfigureApplicationCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

    builder.Services.AddAuthorization(options =>
    {
        options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();
    });

    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentUserScope, CurrentUserScope>();

    builder.Services.AddRazorPages();
    builder.Services.AddHealthChecks();

    var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        await using var scope = app.Services.CreateAsyncScope();
        var initializer = scope.ServiceProvider.GetRequiredService<LoansDatabaseInitializer>();
        await initializer.InitializeAsync(app.Configuration.GetValue<bool>("Database:ResetOnStartup"));
    }
    else
    {
        app.UseExceptionHandler("/Error");
    }

    app.UseSerilogRequestLogging();

    app.UseRouting();

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapStaticAssets().AllowAnonymous();
    app.MapHealthChecks("/health").AllowAnonymous();
    app.MapRazorPages()
       .WithStaticAssets();

    await app.RunAsync();
    return 0;
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>Exposed for <c>WebApplicationFactory</c> in tests.</summary>
public partial class Program;
