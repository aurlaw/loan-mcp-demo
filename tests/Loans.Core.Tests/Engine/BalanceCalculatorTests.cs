using Loans.Core.Engine;

namespace Loans.Core.Tests.Engine;

public class BalanceCalculatorTests
{
    [Fact]
    public void CurrentBalance_ExcludesMissedPayments()
    {
        var loan = TestData.Loan(principalCents: 10_000_000);
        loan.AddPayment(TestData.Payment(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 1), principalCents: 150_000));
        loan.AddPayment(TestData.Payment(new DateOnly(2026, 3, 1), paidDate: null, principalCents: 151_000));
        loan.AddPayment(TestData.Payment(new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 10), principalCents: 152_000));

        Assert.Equal(10_000_000 - 150_000 - 152_000, BalanceCalculator.CurrentBalanceCents(loan));
    }

    [Fact]
    public void PaidThrough_IsLatestDueDateAmongPaidPayments()
    {
        var loan = TestData.Loan();
        loan.AddPayment(TestData.Payment(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 1), 150_000));
        loan.AddPayment(TestData.Payment(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 15), 151_000));
        loan.AddPayment(TestData.Payment(new DateOnly(2026, 4, 1), paidDate: null, principalCents: 152_000));

        Assert.Equal(new DateOnly(2026, 3, 1), BalanceCalculator.PaidThroughDate(loan));
    }

    [Fact]
    public void PaidThrough_WithNoPayments_IsOriginationDate()
    {
        var loan = TestData.Loan(originationDate: new DateOnly(2026, 5, 15));

        Assert.Equal(new DateOnly(2026, 5, 15), BalanceCalculator.PaidThroughDate(loan));
        Assert.Equal(loan.OriginalPrincipalCents, BalanceCalculator.CurrentBalanceCents(loan));
    }
}
