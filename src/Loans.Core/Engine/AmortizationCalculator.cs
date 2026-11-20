using Loans.Core.Common;

namespace Loans.Core.Engine;

public sealed record AmortizationRow(
    int Number,
    DateOnly DueDate,
    long PaymentCents,
    long InterestCents,
    long PrincipalCents,
    long RemainingBalanceCents);

public sealed record AmortizationSchedule(
    long MonthlyPaymentCents,
    long BalloonCents,
    long TotalInterestCents,
    IReadOnlyList<AmortizationRow> Rows);

/// <summary>Fixed-rate, level-payment amortization (30/360 monthly rate).</summary>
public static class AmortizationCalculator
{
    /// <summary>Level monthly payment over <paramref name="amortizationMonths"/>, rounded to cents.</summary>
    public static Result<long> MonthlyPaymentCents(long principalCents, int rateBps, int amortizationMonths)
    {
        if (ValidateInputs(principalCents, rateBps, amortizationMonths, amortizationMonths) is { } error)
        {
            return Result<long>.Failure(error);
        }

        return Result<long>.Success(CalculatePayment(principalCents, rateBps, amortizationMonths));
    }

    public static Result<AmortizationSchedule> Build(
        long principalCents,
        int rateBps,
        int termMonths,
        int amortizationMonths,
        DateOnly firstPaymentDate)
    {
        if (ValidateInputs(principalCents, rateBps, termMonths, amortizationMonths) is { } error)
        {
            return Result<AmortizationSchedule>.Failure(error);
        }

        var monthlyRate = MonthlyRate(rateBps);
        var payment = CalculatePayment(principalCents, rateBps, amortizationMonths);
        var isBalloon = termMonths < amortizationMonths;

        var rows = new List<AmortizationRow>(termMonths);
        var balance = principalCents;
        var totalInterest = 0L;
        var balloon = 0L;

        for (var number = 1; number <= termMonths; number++)
        {
            var interest = DecimalMath.RoundCents(balance * monthlyRate);
            var principal = payment - interest;
            var rowPayment = payment;

            if (number == termMonths)
            {
                if (isBalloon)
                {
                    balloon = balance - principal;
                    rowPayment = payment + balloon;
                }
                else
                {
                    rowPayment = balance + interest;
                }

                principal = balance;
            }

            balance -= principal;
            totalInterest += interest;
            rows.Add(new AmortizationRow(number, firstPaymentDate.AddMonths(number - 1), rowPayment, interest, principal, balance));
        }

        return Result<AmortizationSchedule>.Success(new AmortizationSchedule(payment, balloon, totalInterest, rows));
    }

    private static decimal MonthlyRate(int rateBps) => rateBps / 10_000m / 12m;

    private static long CalculatePayment(long principalCents, int rateBps, int amortizationMonths)
    {
        if (rateBps == 0)
        {
            return DecimalMath.RoundCents((decimal)principalCents / amortizationMonths);
        }

        var r = MonthlyRate(rateBps);
        var discount = 1m - 1m / DecimalMath.Pow(1m + r, amortizationMonths);
        return DecimalMath.RoundCents(principalCents * r / discount);
    }

    private static Error? ValidateInputs(long principalCents, int rateBps, int termMonths, int amortizationMonths)
    {
        if (principalCents <= 0)
        {
            return new Error("amortization.invalid_principal", "Principal must be greater than zero.");
        }

        if (rateBps < 0)
        {
            return new Error("amortization.invalid_rate", "Rate cannot be negative.");
        }

        if (termMonths <= 0 || termMonths > amortizationMonths)
        {
            return new Error("amortization.invalid_term", "Term must be greater than zero and no longer than the amortization.");
        }

        return null;
    }
}
