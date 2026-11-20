using Loans.Core.Domain;

namespace Loans.Core.Tests.Domain;

public class FactoryValidationTests
{
    [Theory]
    [InlineData("pa")]
    [InlineData("PEN")]
    [InlineData("P1")]
    [InlineData("")]
    public void Borrower_InvalidState_Fails(string state)
    {
        var result = Borrower.Create("Acme LLC", EntityType.Llc, "Manufacturing", 5, state);

        Assert.True(result.IsFailure);
        Assert.Equal("borrower.invalid_state", result.Error.Code);
    }

    [Fact]
    public void Borrower_Valid_Succeeds()
    {
        var borrower = TestData.Ok(Borrower.Create("Acme LLC", EntityType.Llc, "Manufacturing", 5, "PA", "user-1"));

        Assert.Equal("PA", borrower.State);
        Assert.Equal("user-1", borrower.LinkedUserId);
    }

    [Fact]
    public void Loan_TermLoanWithBalloon_IsRejected()
    {
        var result = Loan.Create("CL-10001", 1, null, LoanProduct.TermLoan, 10_000_000, 600, 60, 120, new DateOnly(2026, 1, 1), LoanStatus.Current);

        Assert.True(result.IsFailure);
        Assert.Equal("loan.balloon_not_allowed", result.Error.Code);
    }

    [Fact]
    public void Loan_CreMortgageWithBalloon_IsAllowed_AndMaturityIsOriginationPlusTerm()
    {
        var loan = TestData.Ok(Loan.Create("CL-10002", 1, null, LoanProduct.CreMortgage, 100_000_000, 725, 120, 300, new DateOnly(2026, 1, 15), LoanStatus.Current));

        Assert.Equal(new DateOnly(2036, 1, 15), loan.MaturityDate);
    }

    [Fact]
    public void Loan_InvalidAmount_Fails()
    {
        var result = Loan.Create("CL-10003", 1, null, LoanProduct.TermLoan, 0, 600, 60, 60, new DateOnly(2026, 1, 1), LoanStatus.Current);

        Assert.True(result.IsFailure);
        Assert.Equal("loan.invalid_amount", result.Error.Code);
    }

    [Fact]
    public void Payment_PrincipalPlusInterestMustEqualAmount()
    {
        var result = Payment.Create(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 1), 100_000, 60_000, 30_000, PaymentStatus.OnTime);

        Assert.True(result.IsFailure);
        Assert.Equal("payment.amount_mismatch", result.Error.Code);
    }

    [Theory]
    [InlineData(PaymentStatus.Missed, true)]
    [InlineData(PaymentStatus.OnTime, false)]
    [InlineData(PaymentStatus.Late, false)]
    public void Payment_StatusMustAgreeWithPaidDate(PaymentStatus status, bool hasPaidDate)
    {
        DateOnly? paidDate = hasPaidDate ? new DateOnly(2026, 2, 1) : null;

        var result = Payment.Create(new DateOnly(2026, 2, 1), paidDate, 100_000, 70_000, 30_000, status);

        Assert.True(result.IsFailure);
        Assert.Equal("payment.invalid_status", result.Error.Code);
    }

    [Fact]
    public void UnderwritingResult_RiskRatingOutOfRange_Fails()
    {
        var result = UnderwritingResult.Create(150, null, 9, Recommendation.Approve, [], 1_000, TestData.Now, TestData.UnderwriterId);

        Assert.True(result.IsFailure);
        Assert.Equal("underwriting.invalid_risk_rating", result.Error.Code);
    }

    [Fact]
    public void Collateral_NonPositiveValue_Fails()
    {
        var result = Collateral.Create(CollateralType.RealEstate, 0);

        Assert.True(result.IsFailure);
        Assert.Equal("collateral.invalid_value", result.Error.Code);
    }
}
