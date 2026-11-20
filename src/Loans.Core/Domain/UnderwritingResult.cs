using Loans.Core.Common;

namespace Loans.Core.Domain;

/// <summary>Latest underwriting run for an application (one per application; overwritten on rerun).</summary>
public sealed class UnderwritingResult
{
    private List<PolicyException> _exceptions = [];

    private UnderwritingResult()
    {
        RunByUserId = null!;
    }

    public int ApplicationId { get; internal set; }
    public int DscrX100 { get; private set; }
    public int? LtvBps { get; private set; }
    public int RiskRating { get; private set; }
    public Recommendation Recommendation { get; private set; }
    public IReadOnlyList<PolicyException> Exceptions => _exceptions;
    public long ProposedMonthlyPaymentCents { get; private set; }
    public DateTimeOffset RunAt { get; private set; }
    public string RunByUserId { get; private set; }

    public static Result<UnderwritingResult> Create(
        int dscrX100,
        int? ltvBps,
        int riskRating,
        Recommendation recommendation,
        IEnumerable<PolicyException> exceptions,
        long proposedMonthlyPaymentCents,
        DateTimeOffset runAt,
        string runByUserId)
    {
        ArgumentNullException.ThrowIfNull(exceptions);

        if (riskRating is < 1 or > 8)
        {
            return Result<UnderwritingResult>.Failure(new Error("underwriting.invalid_risk_rating", "Risk rating must be between 1 and 8."));
        }

        if (ltvBps is < 0)
        {
            return Result<UnderwritingResult>.Failure(new Error("underwriting.invalid_ltv", "LTV cannot be negative."));
        }

        if (proposedMonthlyPaymentCents <= 0)
        {
            return Result<UnderwritingResult>.Failure(new Error("underwriting.invalid_payment", "Proposed monthly payment must be greater than zero."));
        }

        if (string.IsNullOrWhiteSpace(runByUserId))
        {
            return Result<UnderwritingResult>.Failure(new Error("underwriting.invalid_user", "Run-by user is required."));
        }

        return Result<UnderwritingResult>.Success(new UnderwritingResult
        {
            DscrX100 = dscrX100,
            LtvBps = ltvBps,
            RiskRating = riskRating,
            Recommendation = recommendation,
            _exceptions = [.. exceptions],
            ProposedMonthlyPaymentCents = proposedMonthlyPaymentCents,
            RunAt = runAt,
            RunByUserId = runByUserId,
        });
    }
}
