using Loans.Core.Domain;

namespace Loans.Core.Tests.Domain;

public class ApplicationTests
{
    private static readonly DateTimeOffset DecidedAt = TestData.Now;

    [Fact]
    public void Create_StartsSubmittedWithNoResultOrDecision()
    {
        var app = TestData.Application();

        Assert.Equal(ApplicationStatus.Submitted, app.Status);
        Assert.Null(app.UnderwritingResult);
        Assert.Null(app.Decision);
    }

    [Fact]
    public void Create_TermLoanWithBalloon_IsRejected()
    {
        var result = Application.Create(
            1, TestData.OfficerId, LoanProduct.TermLoan, 10_000_000, 600, 60, 120, "Working capital",
            new DateOnly(2026, 11, 1),
            TestData.Ok(Financials.Create(1, 1, 0)),
            TestData.Ok(Collateral.Create(CollateralType.Equipment, 1)));

        Assert.True(result.IsFailure);
        Assert.Equal("application.balloon_not_allowed", result.Error.Code);
    }

    [Fact]
    public void Create_CreMortgageWithBalloon_IsAllowed()
    {
        var app = TestData.Application(product: LoanProduct.CreMortgage, termMonths: 120, amortizationMonths: 300);

        Assert.Equal(120, app.TermMonths);
        Assert.Equal(300, app.AmortizationMonths);
    }

    [Theory]
    [InlineData(0, 600, 60, 60, "application.invalid_amount")]
    [InlineData(10_000_000, -1, 60, 60, "application.invalid_rate")]
    [InlineData(10_000_000, 600, 0, 60, "application.invalid_term")]
    [InlineData(10_000_000, 600, 61, 60, "application.invalid_term")]
    public void Create_InvalidTerms_Fail(long amount, int rateBps, int term, int amortization, string expectedCode)
    {
        var result = Application.Create(
            1, TestData.OfficerId, LoanProduct.CreMortgage, amount, rateBps, term, amortization, "Acquisition",
            new DateOnly(2026, 11, 1),
            TestData.Ok(Financials.Create(1, 1, 0)),
            TestData.Ok(Collateral.Create(CollateralType.RealEstate, 1)));

        Assert.True(result.IsFailure);
        Assert.Equal(expectedCode, result.Error.Code);
    }

    [Fact]
    public void ApplyUnderwriting_SetsResultAndMovesToInUnderwriting()
    {
        var app = TestData.Application();

        var result = app.ApplyUnderwriting(TestData.UnderwritingResult());

        Assert.True(result.IsSuccess);
        Assert.Equal(ApplicationStatus.InUnderwriting, app.Status);
        Assert.NotNull(app.UnderwritingResult);
    }

    [Fact]
    public void ApplyUnderwriting_Rerun_ReplacesPreviousResult()
    {
        var app = TestData.Application();
        app.ApplyUnderwriting(TestData.UnderwritingResult(dscrX100: 150));

        var second = TestData.UnderwritingResult(dscrX100: 120, recommendation: Recommendation.ApproveWithConditions);
        var result = app.ApplyUnderwriting(second);

        Assert.True(result.IsSuccess);
        Assert.Same(second, app.UnderwritingResult);
        Assert.Equal(120, app.UnderwritingResult!.DscrX100);
    }

    [Fact]
    public void ApplyUnderwriting_AfterDecision_Fails()
    {
        var app = TestData.Application();
        app.ApplyUnderwriting(TestData.UnderwritingResult());
        app.RecordDecision(DecisionOutcome.Decline, null, null, TestData.UnderwriterId, DecidedAt);

        var result = app.ApplyUnderwriting(TestData.UnderwritingResult());

        Assert.True(result.IsFailure);
        Assert.Equal("underwriting.decision_exists", result.Error.Code);
        Assert.Equal(ApplicationStatus.Declined, app.Status);
    }

    [Fact]
    public void RecordDecision_WithoutUnderwriting_Fails()
    {
        var app = TestData.Application();

        var result = app.RecordDecision(DecisionOutcome.Approve, 700, null, TestData.UnderwriterId, DecidedAt);

        Assert.True(result.IsFailure);
        Assert.Equal("decision.not_underwritten", result.Error.Code);
        Assert.Equal(ApplicationStatus.Submitted, app.Status);
    }

    [Fact]
    public void RecordDecision_Twice_Fails()
    {
        var app = TestData.Application();
        app.ApplyUnderwriting(TestData.UnderwritingResult());
        app.RecordDecision(DecisionOutcome.Approve, 700, null, TestData.UnderwriterId, DecidedAt);

        var result = app.RecordDecision(DecisionOutcome.Decline, null, null, TestData.UnderwriterId, DecidedAt);

        Assert.True(result.IsFailure);
        Assert.Equal("decision.already_recorded", result.Error.Code);
        Assert.Equal(ApplicationStatus.Approved, app.Status);
    }

    [Theory]
    [InlineData(DecisionOutcome.Approve)]
    [InlineData(DecisionOutcome.ApproveWithConditions)]
    public void RecordDecision_ApprovalWithoutRate_Fails(DecisionOutcome outcome)
    {
        var app = TestData.Application();
        app.ApplyUnderwriting(TestData.UnderwritingResult());

        var result = app.RecordDecision(outcome, null, "Personal guarantee", TestData.UnderwriterId, DecidedAt);

        Assert.True(result.IsFailure);
        Assert.Equal("decision.rate_required", result.Error.Code);
        Assert.Null(app.Decision);
        Assert.Equal(ApplicationStatus.InUnderwriting, app.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RecordDecision_ApproveWithConditionsWithoutConditions_Fails(string? conditions)
    {
        var app = TestData.Application();
        app.ApplyUnderwriting(TestData.UnderwritingResult());

        var result = app.RecordDecision(DecisionOutcome.ApproveWithConditions, 700, conditions, TestData.UnderwriterId, DecidedAt);

        Assert.True(result.IsFailure);
        Assert.Equal("decision.conditions_required", result.Error.Code);
    }

    [Theory]
    [InlineData(DecisionOutcome.Approve, 700, null, ApplicationStatus.Approved)]
    [InlineData(DecisionOutcome.ApproveWithConditions, 725, "Personal guarantee", ApplicationStatus.Approved)]
    [InlineData(DecisionOutcome.Decline, null, null, ApplicationStatus.Declined)]
    public void RecordDecision_SetsTerminalStatus(DecisionOutcome outcome, int? rateBps, string? conditions, ApplicationStatus expected)
    {
        var app = TestData.Application();
        app.ApplyUnderwriting(TestData.UnderwritingResult());

        var result = app.RecordDecision(outcome, rateBps, conditions, TestData.UnderwriterId, DecidedAt);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, app.Status);
        Assert.NotNull(app.Decision);
        Assert.Equal(outcome, app.Decision!.Outcome);
        Assert.Equal(rateBps, app.Decision.ApprovedRateBps);
        Assert.Equal(TestData.UnderwriterId, app.Decision.DecidedByUserId);
    }
}
