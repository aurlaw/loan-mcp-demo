using Loans.Core.Common;

namespace Loans.Core.Domain;

public sealed class Loan
{
    private readonly List<Payment> _payments = [];

    private Loan()
    {
        LoanNumber = null!;
    }

    public int Id { get; private set; }
    public string LoanNumber { get; private set; }
    public int BorrowerId { get; private set; }
    public int? SourceApplicationId { get; private set; }
    public LoanProduct Product { get; private set; }
    public long OriginalPrincipalCents { get; private set; }
    public int RateBps { get; private set; }
    public int TermMonths { get; private set; }
    public int AmortizationMonths { get; private set; }
    public DateOnly OriginationDate { get; private set; }
    public DateOnly MaturityDate { get; private set; }
    public LoanStatus Status { get; private set; }
    public IReadOnlyCollection<Payment> Payments => _payments;

    /// <summary>Creates a loan. Maturity date is origination date plus the term.</summary>
    public static Result<Loan> Create(
        string loanNumber,
        int borrowerId,
        int? sourceApplicationId,
        LoanProduct product,
        long originalPrincipalCents,
        int rateBps,
        int termMonths,
        int amortizationMonths,
        DateOnly originationDate,
        LoanStatus status)
    {
        if (string.IsNullOrWhiteSpace(loanNumber))
        {
            return Result<Loan>.Failure(new Error("loan.invalid_number", "Loan number is required."));
        }

        if (LoanTerms.Validate("loan", product, originalPrincipalCents, rateBps, termMonths, amortizationMonths) is { } error)
        {
            return Result<Loan>.Failure(error);
        }

        return Result<Loan>.Success(new Loan
        {
            LoanNumber = loanNumber.Trim(),
            BorrowerId = borrowerId,
            SourceApplicationId = sourceApplicationId,
            Product = product,
            OriginalPrincipalCents = originalPrincipalCents,
            RateBps = rateBps,
            TermMonths = termMonths,
            AmortizationMonths = amortizationMonths,
            OriginationDate = originationDate,
            MaturityDate = originationDate.AddMonths(termMonths),
            Status = status,
        });
    }

    public void AddPayment(Payment payment)
    {
        ArgumentNullException.ThrowIfNull(payment);
        payment.LoanId = Id;
        _payments.Add(payment);
    }
}
