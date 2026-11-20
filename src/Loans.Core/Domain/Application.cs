using Loans.Core.Common;

namespace Loans.Core.Domain;

public sealed class Application
{
    private Application()
    {
        AssignedOfficerUserId = null!;
        Purpose = null!;
        Financials = null!;
        Collateral = null!;
    }

    public int Id { get; private set; }
    public int BorrowerId { get; private set; }
    public string AssignedOfficerUserId { get; private set; }
    public LoanProduct Product { get; private set; }
    public long RequestedAmountCents { get; private set; }
    public int RequestedRateBps { get; private set; }
    public int TermMonths { get; private set; }
    public int AmortizationMonths { get; private set; }
    public string Purpose { get; private set; }
    public ApplicationStatus Status { get; private set; }
    public DateOnly SubmittedOn { get; private set; }
    public Financials Financials { get; private set; }
    public Collateral Collateral { get; private set; }
    public UnderwritingResult? UnderwritingResult { get; private set; }
    public Decision? Decision { get; private set; }

    public static Result<Application> Create(
        int borrowerId,
        string assignedOfficerUserId,
        LoanProduct product,
        long requestedAmountCents,
        int requestedRateBps,
        int termMonths,
        int amortizationMonths,
        string purpose,
        DateOnly submittedOn,
        Financials financials,
        Collateral collateral)
    {
        ArgumentNullException.ThrowIfNull(financials);
        ArgumentNullException.ThrowIfNull(collateral);

        if (LoanTerms.Validate("application", product, requestedAmountCents, requestedRateBps, termMonths, amortizationMonths) is { } error)
        {
            return Result<Application>.Failure(error);
        }

        if (string.IsNullOrWhiteSpace(assignedOfficerUserId))
        {
            return Result<Application>.Failure(new Error("application.invalid_officer", "Assigned loan officer is required."));
        }

        if (string.IsNullOrWhiteSpace(purpose))
        {
            return Result<Application>.Failure(new Error("application.invalid_purpose", "Purpose is required."));
        }

        return Result<Application>.Success(new Application
        {
            BorrowerId = borrowerId,
            AssignedOfficerUserId = assignedOfficerUserId,
            Product = product,
            RequestedAmountCents = requestedAmountCents,
            RequestedRateBps = requestedRateBps,
            TermMonths = termMonths,
            AmortizationMonths = amortizationMonths,
            Purpose = purpose.Trim(),
            Status = ApplicationStatus.Submitted,
            SubmittedOn = submittedOn,
            Financials = financials,
            Collateral = collateral,
        });
    }

    /// <summary>Replaces the latest underwriting result and moves the application into underwriting.</summary>
    public Result ApplyUnderwriting(UnderwritingResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (Decision is not null)
        {
            return Result.Failure(new Error("underwriting.decision_exists", "A decision has already been recorded for this application."));
        }

        result.ApplicationId = Id;
        UnderwritingResult = result;
        Status = ApplicationStatus.InUnderwriting;
        return Result.Success();
    }

    /// <summary>Records the credit decision and moves the application to its terminal status.</summary>
    public Result RecordDecision(
        DecisionOutcome outcome,
        int? approvedRateBps,
        string? conditions,
        string decidedByUserId,
        DateTimeOffset decidedAt)
    {
        if (UnderwritingResult is null)
        {
            return Result.Failure(new Error("decision.not_underwritten", "The application must be underwritten before a decision can be recorded."));
        }

        if (Decision is not null)
        {
            return Result.Failure(new Error("decision.already_recorded", "A decision has already been recorded for this application."));
        }

        var decision = Decision.Create(Id, outcome, approvedRateBps, conditions, decidedByUserId, decidedAt);
        if (decision.IsFailure)
        {
            return Result.Failure(decision.Error);
        }

        Decision = decision.Value;
        Status = outcome == DecisionOutcome.Decline ? ApplicationStatus.Declined : ApplicationStatus.Approved;
        return Result.Success();
    }
}
