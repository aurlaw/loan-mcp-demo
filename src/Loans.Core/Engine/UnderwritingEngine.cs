using Loans.Core.Domain;

namespace Loans.Core.Engine;

public sealed record UnderwritingOutcome(
    int DscrX100,
    int? LtvBps,
    int RiskRating,
    Recommendation Recommendation,
    IReadOnlyList<PolicyException> Exceptions,
    long ProposedMonthlyPaymentCents);

/// <summary>Deterministic underwriting against <see cref="UnderwritingPolicy"/>.</summary>
public static class UnderwritingEngine
{
    public static UnderwritingOutcome Evaluate(Application application, Borrower borrower)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(borrower);

        // Application.Create guarantees valid amount/rate/amortization, so this cannot fail.
        var monthlyPayment = AmortizationCalculator
            .MonthlyPaymentCents(application.RequestedAmountCents, application.RequestedRateBps, application.AmortizationMonths)
            .Value;

        var dscr = CalculateDscrX100(
            application.Financials.NetOperatingIncomeCents,
            application.Financials.ExistingAnnualDebtServiceCents,
            monthlyPayment * 12);

        var maxLtv = UnderwritingPolicy.MaxLtvBps(application.Collateral.Type);
        int? ltv = maxLtv is null
            ? null
            : CalculateLtvBps(application.RequestedAmountCents, application.Collateral.AppraisedValueCents);

        var exceptions = new List<PolicyException>();
        if (dscr < UnderwritingPolicy.MinDscrX100)
        {
            exceptions.Add(PolicyException.DscrBelowMinimum);
        }

        if (ltv > maxLtv)
        {
            exceptions.Add(PolicyException.LtvAboveMaximum);
        }

        if (borrower.YearsInBusiness < UnderwritingPolicy.MinYearsInBusiness)
        {
            exceptions.Add(PolicyException.InsufficientTimeInBusiness);
        }

        return new UnderwritingOutcome(
            dscr,
            ltv,
            RiskRating(dscr, exceptions),
            Recommend(dscr, exceptions),
            exceptions,
            monthlyPayment);
    }

    /// <summary>NOI ÷ total annual debt service, × 100, floored (conservative).</summary>
    public static int CalculateDscrX100(long netOperatingIncomeCents, long existingAnnualDebtServiceCents, long proposedAnnualDebtServiceCents)
    {
        var totalDebtService = existingAnnualDebtServiceCents + proposedAnnualDebtServiceCents;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(totalDebtService);
        return (int)decimal.Floor(netOperatingIncomeCents * 100m / totalDebtService);
    }

    /// <summary>Loan amount ÷ appraised value in basis points, ceiling (conservative).</summary>
    public static int CalculateLtvBps(long loanAmountCents, long appraisedValueCents)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(appraisedValueCents);
        return (int)decimal.Ceiling(loanAmountCents * 10_000m / appraisedValueCents);
    }

    /// <summary>Base rating from DSCR band, +1 per LTV / time-in-business exception, capped at 8. Rating 1 is reserved.</summary>
    public static int RiskRating(int dscrX100, IReadOnlyCollection<PolicyException> exceptions)
    {
        ArgumentNullException.ThrowIfNull(exceptions);

        var rating = dscrX100 switch
        {
            >= 200 => 2,
            >= 150 => 3,
            >= 135 => 4,
            >= 125 => 5,
            >= 110 => 6,
            >= 100 => 7,
            _ => 8,
        };

        if (exceptions.Contains(PolicyException.LtvAboveMaximum))
        {
            rating++;
        }

        if (exceptions.Contains(PolicyException.InsufficientTimeInBusiness))
        {
            rating++;
        }

        return Math.Min(rating, 8);
    }

    public static Recommendation Recommend(int dscrX100, IReadOnlyCollection<PolicyException> exceptions)
    {
        ArgumentNullException.ThrowIfNull(exceptions);

        return exceptions.Count switch
        {
            0 => Recommendation.Approve,
            1 when dscrX100 >= 100 => Recommendation.ApproveWithConditions,
            _ => Recommendation.Decline,
        };
    }
}
