using Loans.Core.Common;
using Loans.Core.Domain;

namespace Loans.Core.Engine;

public sealed record PayoffQuote(
    long PrincipalBalanceCents,
    long AccruedInterestCents,
    long PerDiemCents,
    int Days,
    long TotalPayoffCents,
    DateOnly PayoffDate,
    DateOnly PaidThroughDate);

/// <summary>Payoff = derived balance + Actual/360 interest accrued since the paid-through date.</summary>
public static class PayoffCalculator
{
    public static Result<PayoffQuote> Quote(Loan loan, DateOnly payoffDate)
    {
        ArgumentNullException.ThrowIfNull(loan);

        var balance = BalanceCalculator.CurrentBalanceCents(loan);
        if (loan.Status == LoanStatus.PaidOff || balance <= 0)
        {
            return Result<PayoffQuote>.Failure(new Error("payoff.loan_paid_off", "The loan is already paid off."));
        }

        var paidThrough = BalanceCalculator.PaidThroughDate(loan);
        if (payoffDate < paidThrough)
        {
            return Result<PayoffQuote>.Failure(new Error("payoff.date_before_paid_through", "Payoff date cannot be before the paid-through date."));
        }

        var days = payoffDate.DayNumber - paidThrough.DayNumber;
        var dailyInterest = balance * (loan.RateBps / 10_000m) / 360m;
        var accrued = DecimalMath.RoundCents(dailyInterest * days);
        var perDiem = DecimalMath.RoundCents(dailyInterest);

        return Result<PayoffQuote>.Success(new PayoffQuote(
            balance,
            accrued,
            perDiem,
            days,
            balance + accrued,
            payoffDate,
            paidThrough));
    }
}
