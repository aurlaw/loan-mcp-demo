using Loans.Core.Common;

namespace Loans.Core.Domain;

/// <summary>Shared validation for loan structure (applications and booked loans).</summary>
internal static class LoanTerms
{
    public static Error? Validate(string prefix, LoanProduct product, long amountCents, int rateBps, int termMonths, int amortizationMonths)
    {
        if (amountCents <= 0)
        {
            return new Error($"{prefix}.invalid_amount", "Amount must be greater than zero.");
        }

        if (rateBps < 0)
        {
            return new Error($"{prefix}.invalid_rate", "Rate cannot be negative.");
        }

        if (termMonths <= 0 || termMonths > amortizationMonths)
        {
            return new Error($"{prefix}.invalid_term", "Term must be greater than zero and no longer than the amortization.");
        }

        if (product == LoanProduct.TermLoan && termMonths != amortizationMonths)
        {
            return new Error($"{prefix}.balloon_not_allowed", "Term loans must fully amortize over the term (no balloon).");
        }

        return null;
    }
}
