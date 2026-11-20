using Loans.Core.Domain;
using Loans.Web.Infrastructure;

namespace Loans.Web.Tests;

public class FormattingTests
{
    [Fact]
    public void Values_FormatForDisplay()
    {
        Assert.Equal("$1,234.56", Formatting.Money(123_456));
        Assert.Equal("$0.07", Formatting.Money(7));
        Assert.Equal("7.25%", Formatting.Rate(725));
        Assert.Equal("1.50x", Formatting.Dscr(150));
        Assert.Equal("65.00%", Formatting.Ltv(6500));
        Assert.Equal("n/a", Formatting.Ltv(null));
        Assert.Equal("Nov 1, 2026", Formatting.Date(new DateOnly(2026, 11, 1)));
    }

    [Theory]
    [InlineData(Recommendation.ApproveWithConditions, "Approve with conditions")]
    [InlineData(LoanProduct.CreMortgage, "CRE mortgage")]
    [InlineData(LoanProduct.TermLoan, "Term loan")]
    [InlineData(LoanStatus.PastDue, "Past due")]
    [InlineData(ApplicationStatus.InUnderwriting, "In underwriting")]
    [InlineData(PolicyException.DscrBelowMinimum, "DSCR below minimum")]
    [InlineData(EntityType.Llc, "LLC")]
    public void Enums_AreHumanized(object value, string expected)
    {
        Assert.Equal(expected, Formatting.Humanize((Enum)value));
    }

    [Theory]
    [InlineData(LoanStatus.Current, "text-bg-success")]
    [InlineData(ApplicationStatus.Approved, "text-bg-success")]
    [InlineData(LoanStatus.PastDue, "text-bg-danger")]
    [InlineData(ApplicationStatus.Declined, "text-bg-danger")]
    [InlineData(ApplicationStatus.InUnderwriting, "text-bg-warning")]
    [InlineData(ApplicationStatus.Submitted, "text-bg-secondary")]
    [InlineData(LoanStatus.PaidOff, "text-bg-secondary")]
    public void StatusBadges_UseConsistentColors(object status, string expected)
    {
        Assert.Equal(expected, Formatting.BadgeClass((Enum)status));
    }
}
