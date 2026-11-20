using Loans.Core.Domain;
using Loans.Core.Engine;

namespace Loans.Core.Tests.Engine;

public class PayoffCalculatorTests
{
    [Fact]
    public void Quote_100k_725bps_30Days_AccruesActual360Interest()
    {
        var loan = TestData.Loan(principalCents: 10_000_000, rateBps: 725, originationDate: new DateOnly(2026, 1, 1));

        var quote = TestData.Ok(PayoffCalculator.Quote(loan, new DateOnly(2026, 1, 31)));

        Assert.Equal(30, quote.Days);
        Assert.Equal(10_000_000, quote.PrincipalBalanceCents);
        Assert.Equal(60_417, quote.AccruedInterestCents);
        Assert.Equal(10_060_417, quote.TotalPayoffCents);
        Assert.Equal(2_014, quote.PerDiemCents);
        Assert.Equal(new DateOnly(2026, 1, 1), quote.PaidThroughDate);
    }

    [Fact]
    public void Quote_RoundsAccruedInterestOnce_NotFromRoundedPerDiem()
    {
        // Per-diem = $20.138888...; 30 × rounded per-diem ($20.14) would give $604.20, not $604.17.
        var loan = TestData.Loan(principalCents: 10_000_000, rateBps: 725, originationDate: new DateOnly(2026, 1, 1));

        var quote = TestData.Ok(PayoffCalculator.Quote(loan, new DateOnly(2026, 1, 31)));

        Assert.NotEqual(quote.PerDiemCents * quote.Days, quote.AccruedInterestCents);
    }

    [Fact]
    public void Quote_UsesDerivedBalanceAndPaidThroughDate()
    {
        var loan = TestData.Loan(principalCents: 10_000_000, rateBps: 725, originationDate: new DateOnly(2026, 1, 1));
        loan.AddPayment(TestData.Payment(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 1), principalCents: 1_000_000));

        var quote = TestData.Ok(PayoffCalculator.Quote(loan, new DateOnly(2026, 2, 11)));

        Assert.Equal(9_000_000, quote.PrincipalBalanceCents);
        Assert.Equal(new DateOnly(2026, 2, 1), quote.PaidThroughDate);
        Assert.Equal(10, quote.Days);
        Assert.Equal(18_125, quote.AccruedInterestCents); // 90,000 × 0.0725 / 360 × 10 = 181.25
    }

    [Fact]
    public void Quote_OnPaidThroughDate_HasNoAccruedInterest()
    {
        var loan = TestData.Loan(originationDate: new DateOnly(2026, 1, 1));

        var quote = TestData.Ok(PayoffCalculator.Quote(loan, new DateOnly(2026, 1, 1)));

        Assert.Equal(0, quote.Days);
        Assert.Equal(0, quote.AccruedInterestCents);
        Assert.Equal(quote.PrincipalBalanceCents, quote.TotalPayoffCents);
    }

    [Fact]
    public void Quote_BeforePaidThrough_Fails()
    {
        var loan = TestData.Loan(originationDate: new DateOnly(2026, 1, 1));
        loan.AddPayment(TestData.Payment(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 1), 150_000));

        var result = PayoffCalculator.Quote(loan, new DateOnly(2026, 1, 15));

        Assert.True(result.IsFailure);
        Assert.Equal("payoff.date_before_paid_through", result.Error.Code);
    }

    [Fact]
    public void Quote_PaidOffStatus_Fails()
    {
        var loan = TestData.Loan(status: LoanStatus.PaidOff);

        var result = PayoffCalculator.Quote(loan, new DateOnly(2026, 6, 1));

        Assert.True(result.IsFailure);
        Assert.Equal("payoff.loan_paid_off", result.Error.Code);
    }

    [Fact]
    public void Quote_ZeroBalance_Fails()
    {
        var loan = TestData.Loan(principalCents: 300_000);
        loan.AddPayment(TestData.Payment(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 1), 300_000));

        var result = PayoffCalculator.Quote(loan, new DateOnly(2026, 3, 1));

        Assert.True(result.IsFailure);
        Assert.Equal("payoff.loan_paid_off", result.Error.Code);
    }
}
