using System.Collections.Frozen;
using Loans.Core.Data.Seeding;

namespace Loans.Core.Security;

/// <summary>What a user can do when connected through Claude (MCP). Granted by role.</summary>
public enum Capability
{
    LoansRead,
    UnderwritingRun,
    DecisionsWrite,
}

/// <summary>OAuth scope strings for each capability (consumed by the authorization server in Phase 4).</summary>
public static class CapabilityScopes
{
    public const string LoansRead = "loans.read";
    public const string UnderwritingRun = "underwriting.run";
    public const string DecisionsWrite = "decisions.write";

    public static string For(Capability capability) => capability switch
    {
        Capability.LoansRead => LoansRead,
        Capability.UnderwritingRun => UnderwritingRun,
        Capability.DecisionsWrite => DecisionsWrite,
        _ => throw new ArgumentOutOfRangeException(nameof(capability), capability, null),
    };
}

/// <summary>Single source of truth for role → capability mapping.</summary>
public static class RoleCapabilities
{
    private static readonly FrozenDictionary<string, FrozenSet<Capability>> Map =
        new Dictionary<string, FrozenSet<Capability>>(StringComparer.Ordinal)
        {
            [SeedRoles.Borrower] = FrozenSet<Capability>.Empty,
            [SeedRoles.LoanOfficer] = new[] { Capability.LoansRead, Capability.UnderwritingRun }.ToFrozenSet(),
            [SeedRoles.Underwriter] = new[] { Capability.LoansRead, Capability.UnderwritingRun, Capability.DecisionsWrite }.ToFrozenSet(),
            [SeedRoles.Admin] = new[] { Capability.LoansRead }.ToFrozenSet(),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Capabilities for a single role; unknown roles get none.</summary>
    public static IReadOnlySet<Capability> For(string role) =>
        Map.TryGetValue(role, out var capabilities) ? capabilities : FrozenSet<Capability>.Empty;

    /// <summary>Union of capabilities across roles. A Borrower role present means none (Borrower wins).</summary>
    public static IReadOnlySet<Capability> For(IEnumerable<string> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        var list = roles.ToList();

        if (list.Contains(SeedRoles.Borrower, StringComparer.Ordinal))
        {
            return FrozenSet<Capability>.Empty;
        }

        return list.SelectMany(For).ToFrozenSet();
    }

    /// <summary>Whether a user with these roles may connect through Claude (has any capability).</summary>
    public static bool HasMcpAccess(IEnumerable<string> roles) => For(roles).Count > 0;

    /// <summary>Plain-language label for users and consent screens.</summary>
    public static string Label(Capability capability) => capability switch
    {
        Capability.LoansRead => "View loans and applications",
        Capability.UnderwritingRun => "Run underwriting analysis",
        Capability.DecisionsWrite => "Record credit decisions",
        _ => throw new ArgumentOutOfRangeException(nameof(capability), capability, null),
    };

    /// <summary>Labels in a stable display order (enum order).</summary>
    public static IReadOnlyList<string> Labels(IEnumerable<Capability> capabilities) =>
        capabilities.Order().Select(Label).ToList();
}
