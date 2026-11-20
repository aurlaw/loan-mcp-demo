using Loans.Core.Common;

namespace Loans.Core.Domain;

public sealed class Payment
{
    private Payment() { }

    public int Id { get; private set; }
    public int LoanId { get; internal set; }
    public DateOnly DueDate { get; private set; }
    public DateOnly? PaidDate { get; private set; }
    public long AmountCents { get; private set; }
    public long PrincipalCents { get; private set; }
    public long InterestCents { get; private set; }
    public PaymentStatus Status { get; private set; }

    /// <summary>Creates a payment record. Attach it to a loan with <see cref="Loan.AddPayment"/>.</summary>
    public static Result<Payment> Create(
        DateOnly dueDate,
        DateOnly? paidDate,
        long amountCents,
        long principalCents,
        long interestCents,
        PaymentStatus status)
    {
        if (amountCents <= 0 || principalCents < 0 || interestCents < 0)
        {
            return Result<Payment>.Failure(new Error("payment.invalid_amount", "Amount must be positive; principal and interest cannot be negative."));
        }

        if (principalCents + interestCents != amountCents)
        {
            return Result<Payment>.Failure(new Error("payment.amount_mismatch", "Principal plus interest must equal the payment amount."));
        }

        if ((status == PaymentStatus.Missed) != (paidDate is null))
        {
            return Result<Payment>.Failure(new Error("payment.invalid_status", "Missed payments have no paid date; on-time and late payments require one."));
        }

        return Result<Payment>.Success(new Payment
        {
            DueDate = dueDate,
            PaidDate = paidDate,
            AmountCents = amountCents,
            PrincipalCents = principalCents,
            InterestCents = interestCents,
            Status = status,
        });
    }
}
