using Loans.Core.Domain;
using Loans.Core.Engine;

namespace Loans.Core.Tests.Engine;

/// <remarks>
/// Default test application: $1,200,000 at 0% over 120 months → $10,000/month, $120,000/year proposed debt service.
/// </remarks>
public class UnderwritingEngineTests
{
    [Fact]
    public void Dscr_KnownValues_Is150()
    {
        Assert.Equal(150, UnderwritingEngine.CalculateDscrX100(30_000_000, 5_000_000, 15_000_000));
    }

    [Fact]
    public void Evaluate_Noi300k_Existing50k_Proposed150k_GivesDscr150()
    {
        // $1,500,000 at 0% over 120 months → $12,500/month → $150,000/year.
        var app = TestData.Application(noiCents: 30_000_000, existingDebtServiceCents: 5_000_000, requestedAmountCents: 150_000_000);

        var outcome = UnderwritingEngine.Evaluate(app, TestData.Borrower());

        Assert.Equal(1_250_000, outcome.ProposedMonthlyPaymentCents);
        Assert.Equal(150, outcome.DscrX100);
    }

    [Fact]
    public void Dscr_IsFloored()
    {
        // 199.99... → 199
        Assert.Equal(199, UnderwritingEngine.CalculateDscrX100(19_999_999, 0, 10_000_000));
    }

    [Fact]
    public void Evaluate_DscrExactly125_HasNoDscrException()
    {
        var outcome = UnderwritingEngine.Evaluate(TestData.Application(noiCents: 15_000_000), TestData.Borrower());

        Assert.Equal(125, outcome.DscrX100);
        Assert.DoesNotContain(PolicyException.DscrBelowMinimum, outcome.Exceptions);
    }

    [Fact]
    public void Evaluate_Dscr124_HasDscrException()
    {
        var outcome = UnderwritingEngine.Evaluate(TestData.Application(noiCents: 14_880_000), TestData.Borrower());

        Assert.Equal(124, outcome.DscrX100);
        Assert.Contains(PolicyException.DscrBelowMinimum, outcome.Exceptions);
    }

    [Theory]
    [InlineData(CollateralType.RealEstate, 160_000_000, 7500, false)]
    [InlineData(CollateralType.RealEstate, 159_999_999, 7501, true)]
    [InlineData(CollateralType.Equipment, 150_000_000, 8000, false)]
    [InlineData(CollateralType.Equipment, 149_999_999, 8001, true)]
    public void Evaluate_LtvBoundaries(CollateralType type, long appraisedCents, int expectedLtv, bool expectException)
    {
        var app = TestData.Application(collateralType: type, appraisedValueCents: appraisedCents);

        var outcome = UnderwritingEngine.Evaluate(app, TestData.Borrower());

        Assert.Equal(expectedLtv, outcome.LtvBps);
        Assert.Equal(expectException, outcome.Exceptions.Contains(PolicyException.LtvAboveMaximum));
    }

    [Fact]
    public void Ltv_IsCeiled()
    {
        // 7500.00005 → 7501
        Assert.Equal(7501, UnderwritingEngine.CalculateLtvBps(120_000_000, 159_999_999));
    }

    [Fact]
    public void Evaluate_BlanketLien_LtvNotEvaluated()
    {
        var app = TestData.Application(collateralType: CollateralType.BlanketLien, appraisedValueCents: 1);

        var outcome = UnderwritingEngine.Evaluate(app, TestData.Borrower());

        Assert.Null(outcome.LtvBps);
        Assert.DoesNotContain(PolicyException.LtvAboveMaximum, outcome.Exceptions);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public void Evaluate_TimeInBusinessBoundary(int years, bool expectException)
    {
        var outcome = UnderwritingEngine.Evaluate(TestData.Application(), TestData.Borrower(yearsInBusiness: years));

        Assert.Equal(expectException, outcome.Exceptions.Contains(PolicyException.InsufficientTimeInBusiness));
    }

    [Theory]
    [InlineData(250, 2)]
    [InlineData(200, 2)]
    [InlineData(199, 3)]
    [InlineData(150, 3)]
    [InlineData(135, 4)]
    [InlineData(125, 5)]
    [InlineData(124, 6)]
    [InlineData(110, 6)]
    [InlineData(100, 7)]
    [InlineData(99, 8)]
    public void RiskRating_BaseBandsFromDscr(int dscr, int expected)
    {
        Assert.Equal(expected, UnderwritingEngine.RiskRating(dscr, []));
    }

    [Fact]
    public void RiskRating_AddsOnePerLtvAndTimeInBusinessException()
    {
        Assert.Equal(5, UnderwritingEngine.RiskRating(150, [PolicyException.LtvAboveMaximum, PolicyException.InsufficientTimeInBusiness]));
    }

    [Fact]
    public void RiskRating_DscrExceptionDoesNotAddBeyondBand()
    {
        Assert.Equal(6, UnderwritingEngine.RiskRating(120, [PolicyException.DscrBelowMinimum]));
    }

    [Fact]
    public void RiskRating_IsCappedAt8()
    {
        Assert.Equal(8, UnderwritingEngine.RiskRating(
            95,
            [PolicyException.DscrBelowMinimum, PolicyException.LtvAboveMaximum, PolicyException.InsufficientTimeInBusiness]));
    }

    [Fact]
    public void Evaluate_WorstCase_RatesAt8AndDeclines()
    {
        // DSCR 95, LTV exception, 1 year in business.
        var app = TestData.Application(noiCents: 11_400_000, collateralType: CollateralType.RealEstate, appraisedValueCents: 120_000_000);

        var outcome = UnderwritingEngine.Evaluate(app, TestData.Borrower(yearsInBusiness: 1));

        Assert.Equal(95, outcome.DscrX100);
        Assert.Equal(3, outcome.Exceptions.Count);
        Assert.Equal(8, outcome.RiskRating);
        Assert.Equal(Recommendation.Decline, outcome.Recommendation);
    }

    [Fact]
    public void Evaluate_Clean_RecommendsApprove()
    {
        var app = TestData.Application(noiCents: 30_000_000, collateralType: CollateralType.RealEstate, appraisedValueCents: 200_000_000);

        var outcome = UnderwritingEngine.Evaluate(app, TestData.Borrower());

        Assert.Empty(outcome.Exceptions);
        Assert.Equal(250, outcome.DscrX100);
        Assert.Equal(2, outcome.RiskRating);
        Assert.Equal(Recommendation.Approve, outcome.Recommendation);
    }

    [Fact]
    public void Evaluate_SingleLtvExceptionWithDscrAbove100_RecommendsApproveWithConditions()
    {
        var app = TestData.Application(noiCents: 30_000_000, collateralType: CollateralType.RealEstate, appraisedValueCents: 150_000_000);

        var outcome = UnderwritingEngine.Evaluate(app, TestData.Borrower());

        Assert.Equal([PolicyException.LtvAboveMaximum], outcome.Exceptions);
        Assert.Equal(3, outcome.RiskRating);
        Assert.Equal(Recommendation.ApproveWithConditions, outcome.Recommendation);
    }

    [Fact]
    public void Recommend_DscrBelow100_Declines()
    {
        Assert.Equal(Recommendation.Decline, UnderwritingEngine.Recommend(99, [PolicyException.DscrBelowMinimum]));
    }

    [Fact]
    public void Recommend_TwoExceptions_Declines()
    {
        Assert.Equal(
            Recommendation.Decline,
            UnderwritingEngine.Recommend(150, [PolicyException.LtvAboveMaximum, PolicyException.InsufficientTimeInBusiness]));
    }

    [Fact]
    public void Evaluate_ProposedPayment_UsesRequestedRateAndAmortization()
    {
        var app = TestData.Application(
            requestedAmountCents: 10_000_000,
            rateBps: 600,
            termMonths: 60,
            amortizationMonths: 60);

        var outcome = UnderwritingEngine.Evaluate(app, TestData.Borrower());

        Assert.Equal(193_328, outcome.ProposedMonthlyPaymentCents);
    }
}
