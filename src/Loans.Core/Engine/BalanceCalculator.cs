using Loans.Core.Domain;

namespace Loans.Core.Engine;

/// <summary>Balance is derived from posted payments, never stored.</summary>
public static class BalanceCalculator
{
    public static long CurrentBalanceCents(Loan loan)
    {
        ArgumentNullException.ThrowIfNull(loan);
        return loan.OriginalPrincipalCents - loan.Payments.Where(p => p.PaidDate is not null).Sum(p => p.PrincipalCents);
    }

    public static DateOnly PaidThroughDate(Loan loan)
    {
        ArgumentNullException.ThrowIfNull(loan);
        var paid = loan.Payments.Where(p => p.PaidDate is not null).ToList();
        return paid.Count == 0 ? loan.OriginationDate : paid.Max(p => p.DueDate);
    }
}
