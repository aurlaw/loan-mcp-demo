namespace Loans.Core.Data.Seeding;

public static class SeedRoles
{
    public const string Borrower = "Borrower";
    public const string LoanOfficer = "LoanOfficer";
    public const string Underwriter = "Underwriter";
    public const string Admin = "Admin";

    public static readonly IReadOnlyList<string> All = [Borrower, LoanOfficer, Underwriter, Admin];
}

public sealed record SeedUser(string Email, string DisplayName, string Role);

public static class SeedUsers
{
    public static readonly SeedUser Borrower = new("borrower@loans.test", "Dana Whitfield", SeedRoles.Borrower);
    public static readonly SeedUser Officer = new("officer@loans.test", "Marcus Hale", SeedRoles.LoanOfficer);
    public static readonly SeedUser Underwriter = new("underwriter@loans.test", "Priya Raman", SeedRoles.Underwriter);
    public static readonly SeedUser Admin = new("admin@loans.test", "Sam Okafor", SeedRoles.Admin);

    public static readonly IReadOnlyList<SeedUser> All = [Borrower, Officer, Underwriter, Admin];
}

/// <summary>Seeded borrower legal names (each borrower has at most one seeded application).</summary>
public static class SeedBorrowers
{
    public const string AlleghenyPrecision = "Allegheny Precision Machining LLC";
    public const string ThreeRiversDental = "Three Rivers Dental Group, P.C.";
    public const string KeystoneFreight = "Keystone Freight Logistics, Inc.";
    public const string EmberAndOak = "Ember & Oak Restaurant Group LLC";
    public const string MonongahelaProperty = "Monongahela Property Holdings LP";
    public const string RidgelineBuilders = "Ridgeline Builders, Inc.";
    public const string IronworksBrewing = "Ironworks Brewing Company LLC";
    public const string LaurelHighlandsVet = "Laurel Highlands Veterinary Clinic LP";

    /// <summary>The borrower linked to <see cref="SeedUsers.Borrower"/>.</summary>
    public const string Linked = AlleghenyPrecision;
}

public sealed record SeedSettings(string? DefaultPassword);
