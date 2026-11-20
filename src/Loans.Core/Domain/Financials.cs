using Loans.Core.Common;

namespace Loans.Core.Domain;

/// <summary>Single-snapshot borrower financials (owned by <see cref="Application"/>).</summary>
public sealed record Financials
{
    private Financials() { }

    public long AnnualRevenueCents { get; private init; }
    public long NetOperatingIncomeCents { get; private init; }
    public long ExistingAnnualDebtServiceCents { get; private init; }

    public static Result<Financials> Create(long annualRevenueCents, long netOperatingIncomeCents, long existingAnnualDebtServiceCents)
    {
        if (annualRevenueCents < 0)
        {
            return Result<Financials>.Failure(new Error("financials.invalid_revenue", "Annual revenue cannot be negative."));
        }

        if (existingAnnualDebtServiceCents < 0)
        {
            return Result<Financials>.Failure(new Error("financials.invalid_debt_service", "Existing debt service cannot be negative."));
        }

        return Result<Financials>.Success(new Financials
        {
            AnnualRevenueCents = annualRevenueCents,
            NetOperatingIncomeCents = netOperatingIncomeCents,
            ExistingAnnualDebtServiceCents = existingAnnualDebtServiceCents,
        });
    }
}
