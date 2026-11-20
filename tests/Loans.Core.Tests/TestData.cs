using Loans.Core.Common;
using Loans.Core.Domain;

namespace Loans.Core.Tests;

internal static class TestData
{
    public const string OfficerId = "officer-1";
    public const string UnderwriterId = "underwriter-1";
    public static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    public static T Ok<T>(Result<T> result)
    {
        Assert.True(result.IsSuccess, result.Error?.ToString());
        return result.Value;
    }

    public static Borrower Borrower(int yearsInBusiness = 10) =>
        Ok(global::Loans.Core.Domain.Borrower.Create("Acme Fabrication LLC", EntityType.Llc, "Manufacturing", yearsInBusiness, "PA"));

    /// <summary>
    /// Zero-rate term loan by default so proposed debt service is exact:
    /// $1,200,000 over 120 months = $10,000/month = $120,000/year.
    /// </summary>
    public static Application Application(
        long noiCents = 30_000_000,
        long existingDebtServiceCents = 0,
        long requestedAmountCents = 120_000_000,
        int rateBps = 0,
        CollateralType collateralType = CollateralType.BlanketLien,
        long appraisedValueCents = 1_000_000_000,
        LoanProduct product = LoanProduct.TermLoan,
        int termMonths = 120,
        int amortizationMonths = 120) =>
        Ok(global::Loans.Core.Domain.Application.Create(
            borrowerId: 1,
            assignedOfficerUserId: OfficerId,
            product: product,
            requestedAmountCents: requestedAmountCents,
            requestedRateBps: rateBps,
            termMonths: termMonths,
            amortizationMonths: amortizationMonths,
            purpose: "Equipment purchase",
            submittedOn: new DateOnly(2026, 11, 1),
            financials: Ok(Financials.Create(100_000_000, noiCents, existingDebtServiceCents)),
            collateral: Ok(Collateral.Create(collateralType, appraisedValueCents))));

    public static UnderwritingResult UnderwritingResult(int dscrX100 = 150, Recommendation recommendation = Recommendation.Approve) =>
        Ok(global::Loans.Core.Domain.UnderwritingResult.Create(dscrX100, null, 3, recommendation, [], 1_000_000, Now, UnderwriterId));

    public static Loan Loan(
        long principalCents = 10_000_000,
        int rateBps = 725,
        DateOnly? originationDate = null,
        LoanStatus status = LoanStatus.Current) =>
        Ok(global::Loans.Core.Domain.Loan.Create(
            "CL-10001",
            borrowerId: 1,
            sourceApplicationId: null,
            LoanProduct.TermLoan,
            principalCents,
            rateBps,
            termMonths: 60,
            amortizationMonths: 60,
            originationDate ?? new DateOnly(2026, 1, 1),
            status));

    public static Payment Payment(DateOnly dueDate, DateOnly? paidDate, long principalCents, long interestCents = 50_000, PaymentStatus? status = null) =>
        Ok(global::Loans.Core.Domain.Payment.Create(
            dueDate,
            paidDate,
            principalCents + interestCents,
            principalCents,
            interestCents,
            status ?? (paidDate is null ? PaymentStatus.Missed : PaymentStatus.OnTime)));
}
