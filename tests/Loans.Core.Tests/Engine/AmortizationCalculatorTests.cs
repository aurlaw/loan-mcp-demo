using Loans.Core.Engine;

namespace Loans.Core.Tests.Engine;

public class AmortizationCalculatorTests
{
    private static readonly DateOnly FirstPayment = new(2026, 1, 31);

    [Fact]
    public void FullyAmortizing_100k_6pct_60mo_HasKnownPaymentAndZeroFinalBalance()
    {
        var schedule = TestData.Ok(AmortizationCalculator.Build(10_000_000, 600, 60, 60, FirstPayment));

        Assert.Equal(193_328, schedule.MonthlyPaymentCents);
        Assert.Equal(60, schedule.Rows.Count);
        Assert.Equal(0, schedule.Rows[^1].RemainingBalanceCents);
        Assert.Equal(10_000_000, schedule.Rows.Sum(r => r.PrincipalCents));
        Assert.Equal(0, schedule.BalloonCents);
        Assert.All(schedule.Rows.SkipLast(1), r => Assert.Equal(193_328, r.PaymentCents));
        Assert.Equal(schedule.Rows.Sum(r => r.InterestCents), schedule.TotalInterestCents);
        Assert.All(schedule.Rows, r => Assert.Equal(r.PaymentCents, r.PrincipalCents + r.InterestCents));
    }

    [Fact]
    public void DueDates_AreMonthlyFromFirstPaymentDate_WithoutDrift()
    {
        var schedule = TestData.Ok(AmortizationCalculator.Build(10_000_000, 600, 3, 3, new DateOnly(2026, 1, 31)));

        Assert.Equal(
            [new DateOnly(2026, 1, 31), new DateOnly(2026, 2, 28), new DateOnly(2026, 3, 31)],
            schedule.Rows.Select(r => r.DueDate));
    }

    [Fact]
    public void ZeroRate_PaymentIsPrincipalOverN_AndFinalBalanceIsZero()
    {
        var schedule = TestData.Ok(AmortizationCalculator.Build(1_000_000, 0, 3, 3, FirstPayment));

        Assert.Equal(333_333, schedule.MonthlyPaymentCents);
        Assert.Equal(0, schedule.TotalInterestCents);
        Assert.Equal(333_334, schedule.Rows[^1].PrincipalCents);
        Assert.Equal(0, schedule.Rows[^1].RemainingBalanceCents);
    }

    [Fact]
    public void CreBalloon_120Term300Amort_HasBalloonEqualToRemainingAfterRegularPrincipal()
    {
        var schedule = TestData.Ok(AmortizationCalculator.Build(100_000_000, 725, 120, 300, FirstPayment));

        Assert.Equal(120, schedule.Rows.Count);
        Assert.True(schedule.BalloonCents > 0);

        var balanceAfter119 = schedule.Rows[118].RemainingBalanceCents;
        var last = schedule.Rows[^1];
        var regularPrincipal = schedule.MonthlyPaymentCents - last.InterestCents;

        Assert.Equal(balanceAfter119 - regularPrincipal, schedule.BalloonCents);
        Assert.Equal(schedule.MonthlyPaymentCents + schedule.BalloonCents, last.PaymentCents);
        Assert.Equal(balanceAfter119, last.PrincipalCents);
        Assert.Equal(0, last.RemainingBalanceCents);
    }

    [Theory]
    [InlineData(0, 600, 60, 60, "amortization.invalid_principal")]
    [InlineData(-1, 600, 60, 60, "amortization.invalid_principal")]
    [InlineData(10_000_000, -1, 60, 60, "amortization.invalid_rate")]
    [InlineData(10_000_000, 600, 61, 60, "amortization.invalid_term")]
    [InlineData(10_000_000, 600, 0, 60, "amortization.invalid_term")]
    public void InvalidInputs_Fail(long principal, int rateBps, int term, int amortization, string expectedCode)
    {
        var result = AmortizationCalculator.Build(principal, rateBps, term, amortization, FirstPayment);

        Assert.True(result.IsFailure);
        Assert.Equal(expectedCode, result.Error.Code);
    }
}
