using Loans.Core.Common;

namespace Loans.Core.Domain;

/// <summary>Credit decision for an application (one per application). Created via <see cref="Application.RecordDecision"/>.</summary>
public sealed class Decision
{
    private Decision()
    {
        DecidedByUserId = null!;
    }

    public int ApplicationId { get; private set; }
    public DecisionOutcome Outcome { get; private set; }
    public int? ApprovedRateBps { get; private set; }
    public string? Conditions { get; private set; }
    public string DecidedByUserId { get; private set; }
    public DateTimeOffset DecidedAt { get; private set; }

    internal static Result<Decision> Create(
        int applicationId,
        DecisionOutcome outcome,
        int? approvedRateBps,
        string? conditions,
        string decidedByUserId,
        DateTimeOffset decidedAt)
    {
        var isApproval = outcome is DecisionOutcome.Approve or DecisionOutcome.ApproveWithConditions;

        if (isApproval && approvedRateBps is null)
        {
            return Result<Decision>.Failure(new Error("decision.rate_required", "An approved rate is required for approvals."));
        }

        if (approvedRateBps is < 0)
        {
            return Result<Decision>.Failure(new Error("decision.invalid_rate", "Approved rate cannot be negative."));
        }

        if (outcome == DecisionOutcome.ApproveWithConditions && string.IsNullOrWhiteSpace(conditions))
        {
            return Result<Decision>.Failure(new Error("decision.conditions_required", "Conditions are required when approving with conditions."));
        }

        if (string.IsNullOrWhiteSpace(decidedByUserId))
        {
            return Result<Decision>.Failure(new Error("decision.invalid_user", "Decided-by user is required."));
        }

        return Result<Decision>.Success(new Decision
        {
            ApplicationId = applicationId,
            Outcome = outcome,
            ApprovedRateBps = approvedRateBps,
            Conditions = string.IsNullOrWhiteSpace(conditions) ? null : conditions.Trim(),
            DecidedByUserId = decidedByUserId,
            DecidedAt = decidedAt,
        });
    }
}
