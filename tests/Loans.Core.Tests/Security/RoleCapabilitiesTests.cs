using Loans.Core.Data.Seeding;
using Loans.Core.Security;

namespace Loans.Core.Tests.Security;

public class RoleCapabilitiesTests
{
    [Fact]
    public void Map_MatchesRoleTableExactly()
    {
        Assert.Empty(RoleCapabilities.For(SeedRoles.Borrower));
        Assert.Equal([Capability.LoansRead, Capability.UnderwritingRun], RoleCapabilities.For(SeedRoles.LoanOfficer).Order());
        Assert.Equal([Capability.LoansRead, Capability.UnderwritingRun, Capability.DecisionsWrite], RoleCapabilities.For(SeedRoles.Underwriter).Order());
        Assert.Equal([Capability.LoansRead], RoleCapabilities.For(SeedRoles.Admin).Order());
    }

    [Theory]
    [InlineData(SeedRoles.Borrower, false)]
    [InlineData(SeedRoles.LoanOfficer, true)]
    [InlineData(SeedRoles.Underwriter, true)]
    [InlineData(SeedRoles.Admin, true)]
    public void McpAccess_MatchesRoleTable(string role, bool expected)
    {
        Assert.Equal(expected, RoleCapabilities.HasMcpAccess([role]));
    }

    [Fact]
    public void UnknownRoleOrNoRoles_HasNothing()
    {
        Assert.Empty(RoleCapabilities.For("Auditor"));
        Assert.Empty(RoleCapabilities.For(Array.Empty<string>()));
        Assert.False(RoleCapabilities.HasMcpAccess([]));
    }

    [Fact]
    public void MultipleRoles_AreUnioned()
    {
        Assert.Equal(
            [Capability.LoansRead, Capability.UnderwritingRun],
            RoleCapabilities.For([SeedRoles.Admin, SeedRoles.LoanOfficer]).Order());
    }

    [Fact]
    public void BorrowerRolePresent_MeansNoCapabilities()
    {
        Assert.Empty(RoleCapabilities.For([SeedRoles.Underwriter, SeedRoles.Borrower]));
        Assert.False(RoleCapabilities.HasMcpAccess([SeedRoles.Admin, SeedRoles.Borrower]));
    }

    [Fact]
    public void Scopes_AndLabels_MatchCapabilities()
    {
        Assert.Equal("loans.read", CapabilityScopes.For(Capability.LoansRead));
        Assert.Equal("underwriting.run", CapabilityScopes.For(Capability.UnderwritingRun));
        Assert.Equal("decisions.write", CapabilityScopes.For(Capability.DecisionsWrite));

        Assert.Equal("View loans and applications", RoleCapabilities.Label(Capability.LoansRead));
        Assert.Equal("Run underwriting analysis", RoleCapabilities.Label(Capability.UnderwritingRun));
        Assert.Equal("Record credit decisions", RoleCapabilities.Label(Capability.DecisionsWrite));

        Assert.Equal(
            ["View loans and applications", "Run underwriting analysis", "Record credit decisions"],
            RoleCapabilities.Labels(RoleCapabilities.For(SeedRoles.Underwriter)));
    }
}
