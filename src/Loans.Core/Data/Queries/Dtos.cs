using Loans.Core.Domain;

namespace Loans.Core.Data.Queries;

public sealed record BorrowerSummary(int Id, string LegalName, EntityType EntityType, string Industry, int YearsInBusiness, string State);

// Loans

public sealed record LoanSearch(
    string? Text = null,
    LoanStatus? Status = null,
    LoanProduct? Product = null,
    int? BorrowerId = null,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize);

public sealed record LoanSummary(
    int Id,
    string LoanNumber,
    int BorrowerId,
    string BorrowerName,
    LoanProduct Product,
    long OriginalPrincipalCents,
    long CurrentBalanceCents,
    int RateBps,
    DateOnly MaturityDate,
    LoanStatus Status);

public sealed record PaymentDto(
    int Id,
    DateOnly DueDate,
    DateOnly? PaidDate,
    long AmountCents,
    long PrincipalCents,
    long InterestCents,
    PaymentStatus Status);

public sealed record LoanDetail(
    int Id,
    string LoanNumber,
    BorrowerSummary Borrower,
    int? SourceApplicationId,
    LoanProduct Product,
    long OriginalPrincipalCents,
    int RateBps,
    int TermMonths,
    int AmortizationMonths,
    DateOnly OriginationDate,
    DateOnly MaturityDate,
    LoanStatus Status,
    long CurrentBalanceCents,
    DateOnly PaidThroughDate,
    IReadOnlyList<PaymentDto> Payments);

public sealed record ScheduleRowDto(
    int Number,
    DateOnly DueDate,
    long PaymentCents,
    long InterestCents,
    long PrincipalCents,
    long RemainingBalanceCents,
    bool IsPaid,
    bool IsBalloon);

public sealed record LoanSchedule(
    int LoanId,
    string LoanNumber,
    long MonthlyPaymentCents,
    long BalloonCents,
    long TotalInterestCents,
    IReadOnlyList<ScheduleRowDto> Rows);

public sealed record PayoffQuoteDto(
    int LoanId,
    string LoanNumber,
    long PrincipalBalanceCents,
    long AccruedInterestCents,
    long PerDiemCents,
    int Days,
    long TotalPayoffCents,
    DateOnly PayoffDate,
    DateOnly PaidThroughDate);

// Dashboard

public sealed record PastDueLoan(int LoanId, string LoanNumber, int BorrowerId, string BorrowerName, long CurrentBalanceCents, DateOnly? OldestMissedDueDate);

public sealed record DashboardSummary(
    IReadOnlyDictionary<LoanStatus, int> LoansByStatus,
    IReadOnlyDictionary<ApplicationStatus, int> ApplicationsByStatus,
    long TotalOutstandingCents,
    IReadOnlyList<PastDueLoan> PastDueLoans);

// Applications

public sealed record ApplicationSearch(
    string? Text = null,
    ApplicationStatus? Status = null,
    int? BorrowerId = null,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize);

public sealed record ApplicationSummary(
    int Id,
    int BorrowerId,
    string BorrowerName,
    LoanProduct Product,
    long RequestedAmountCents,
    string Purpose,
    ApplicationStatus Status,
    DateOnly SubmittedOn,
    Recommendation? Recommendation);

public sealed record FinancialsDto(long AnnualRevenueCents, long NetOperatingIncomeCents, long ExistingAnnualDebtServiceCents);

public sealed record CollateralDto(CollateralType Type, long AppraisedValueCents);

public sealed record UnderwritingResultDto(
    int DscrX100,
    int? LtvBps,
    int RiskRating,
    Recommendation Recommendation,
    IReadOnlyList<PolicyException> Exceptions,
    long ProposedMonthlyPaymentCents,
    DateTimeOffset RunAt,
    string RunByUserId,
    string? RunByName);

public sealed record DecisionDto(
    DecisionOutcome Outcome,
    int? ApprovedRateBps,
    string? Conditions,
    string DecidedByUserId,
    string? DecidedByName,
    DateTimeOffset DecidedAt);

public sealed record ApplicationDetail(
    int Id,
    BorrowerSummary Borrower,
    string AssignedOfficerUserId,
    string? AssignedOfficerName,
    LoanProduct Product,
    long RequestedAmountCents,
    int RequestedRateBps,
    int TermMonths,
    int AmortizationMonths,
    string Purpose,
    ApplicationStatus Status,
    DateOnly SubmittedOn,
    FinancialsDto Financials,
    CollateralDto Collateral,
    UnderwritingResultDto? UnderwritingResult,
    DecisionDto? Decision);
