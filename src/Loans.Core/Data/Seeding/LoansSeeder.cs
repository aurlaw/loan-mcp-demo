using Loans.Core.Common;
using Loans.Core.Domain;
using Loans.Core.Engine;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Loans.Core.Data.Seeding;

/// <summary>
/// Idempotent, deterministic demo seed. Dates are relative to <c>anchor</c> (first day of the current UTC month);
/// only installments due on or before the anchor are stored.
/// </summary>
public sealed class LoansSeeder(
    LoansDbContext db,
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    SeedSettings settings,
    TimeProvider timeProvider,
    ILogger<LoansSeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await EnsureRolesAsync();
        var users = await EnsureUsersAsync();

        if (await db.Borrowers.AnyAsync(cancellationToken))
        {
            logger.LogInformation("Domain data already present; skipping domain seed");
            return;
        }

        var now = timeProvider.GetUtcNow();
        var anchor = new DateOnly(now.Year, now.Month, 1);

        await SeedDomainAsync(anchor, users, cancellationToken);
        logger.LogInformation("Seeded demo data anchored at {Anchor}", anchor);
    }

    private async Task EnsureRolesAsync()
    {
        foreach (var role in SeedRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                ThrowIfFailed(await roleManager.CreateAsync(new IdentityRole(role)), $"create role {role}");
            }
        }
    }

    private async Task<Dictionary<string, ApplicationUser>> EnsureUsersAsync()
    {
        var result = new Dictionary<string, ApplicationUser>();

        foreach (var seed in SeedUsers.All)
        {
            var user = await userManager.FindByEmailAsync(seed.Email);
            if (user is null)
            {
                if (string.IsNullOrWhiteSpace(settings.DefaultPassword))
                {
                    throw new InvalidOperationException(
                        "Seed:DefaultPassword is not configured but seed users must be created. " +
                        "Set it with: dotnet user-secrets set \"Seed:DefaultPassword\" \"<demo password>\" --project src/Loans.Web");
                }

                user = new ApplicationUser
                {
                    UserName = seed.Email,
                    Email = seed.Email,
                    EmailConfirmed = true,
                    DisplayName = seed.DisplayName,
                };
                ThrowIfFailed(await userManager.CreateAsync(user, settings.DefaultPassword), $"create user {seed.Email}");
            }

            if (!await userManager.IsInRoleAsync(user, seed.Role))
            {
                ThrowIfFailed(await userManager.AddToRoleAsync(user, seed.Role), $"add {seed.Email} to role {seed.Role}");
            }

            result[seed.Email] = user;
        }

        return result;
    }

    private async Task SeedDomainAsync(DateOnly anchor, Dictionary<string, ApplicationUser> users, CancellationToken cancellationToken)
    {
        var officerId = users[SeedUsers.Officer.Email].Id;
        var underwriterId = users[SeedUsers.Underwriter.Email].Id;
        var borrowerUserId = users[SeedUsers.Borrower.Email].Id;

        // Borrowers
        var allegheny = Require(Borrower.Create(SeedBorrowers.AlleghenyPrecision, EntityType.Llc, "Manufacturing", 18, "PA", borrowerUserId));
        var dental = Require(Borrower.Create(SeedBorrowers.ThreeRiversDental, EntityType.Corporation, "Dental Practice", 12, "PA"));
        var freight = Require(Borrower.Create(SeedBorrowers.KeystoneFreight, EntityType.Corporation, "Logistics", 9, "OH"));
        var ember = Require(Borrower.Create(SeedBorrowers.EmberAndOak, EntityType.Llc, "Restaurant Group", 6, "PA"));
        var monongahela = Require(Borrower.Create(SeedBorrowers.MonongahelaProperty, EntityType.Partnership, "Property Holding", 22, "PA"));
        var ridgeline = Require(Borrower.Create(SeedBorrowers.RidgelineBuilders, EntityType.Corporation, "Construction", 14, "WV"));
        var ironworks = Require(Borrower.Create(SeedBorrowers.IronworksBrewing, EntityType.Llc, "Brewery", 1, "PA"));
        var laurel = Require(Borrower.Create(SeedBorrowers.LaurelHighlandsVet, EntityType.Partnership, "Veterinary Clinic", 7, "PA"));

        db.Borrowers.AddRange(allegheny, dental, freight, ember, monongahela, ridgeline, ironworks, laurel);
        await db.SaveChangesAsync(cancellationToken);

        // Applications — numbers engineered for specific UnderwritingEngine outcomes (guarded by tests).
        // A1: DSCR 1.50x, LTV 65% → Approve
        var a1 = NewApplication(allegheny, officerId, LoanProduct.CreMortgage, 130_000_000, 700, 120, 300,
            "Purchase of owner-occupied manufacturing facility", anchor.AddDays(-6),
            revenue: 640_000_000, noi: 22_000_000, existingDebtService: 3_600_000, CollateralType.RealEstate, 200_000_000);
        // A2: DSCR 1.15x, LTV 68% → ApproveWithConditions (DSCR exception only)
        var a2 = NewApplication(ember, officerId, LoanProduct.CreMortgage, 85_000_000, 750, 120, 300,
            "Acquire building for third restaurant location", anchor.AddDays(-9),
            revenue: 410_000_000, noi: 10_750_000, existingDebtService: 1_800_000, CollateralType.RealEstate, 125_000_000);
        // A3: Equipment LTV 85% → ApproveWithConditions (LTV exception only)
        var a3 = NewApplication(freight, officerId, LoanProduct.TermLoan, 42_500_000, 800, 60, 60,
            "Fleet expansion: eight Class 8 tractors", anchor.AddDays(-12),
            revenue: 1_250_000_000, noi: 25_000_000, existingDebtService: 6_000_000, CollateralType.Equipment, 50_000_000);
        // A4: DSCR 0.91x and LTV 82.8% → Decline
        var a4 = NewApplication(monongahela, officerId, LoanProduct.CreMortgage, 240_000_000, 775, 120, 300,
            "Refinance and renovation of mixed-use retail center", anchor.AddDays(-15),
            revenue: 380_000_000, noi: 21_000_000, existingDebtService: 1_200_000, CollateralType.RealEstate, 290_000_000);
        // A5: 1 year in business, blanket lien, DSCR 1.60x → ApproveWithConditions; left in underwriting
        var a5 = NewApplication(ironworks, officerId, LoanProduct.TermLoan, 35_000_000, 850, 84, 84,
            "Brewhouse expansion and canning line", anchor.AddDays(-20),
            revenue: 180_000_000, noi: 10_700_000, existingDebtService: 0, CollateralType.BlanketLien, 60_000_000);
        // A6: DSCR 0.74x, LTV 83.3% → Decline; already declined
        var a6 = NewApplication(ridgeline, officerId, LoanProduct.TermLoan, 60_000_000, 900, 60, 60,
            "Purchase of excavators and site equipment", anchor.AddDays(-40),
            revenue: 520_000_000, noi: 13_000_000, existingDebtService: 2_400_000, CollateralType.Equipment, 72_000_000);

        db.Applications.AddRange(a1, a2, a3, a4, a5, a6);

        // Loans
        db.Loans.AddRange(
            NewLoan("CL-10001", allegheny, LoanProduct.TermLoan, 45_000_000, 675, 60, 60, anchor, monthsAgo: 24, LoanStatus.Current),
            NewLoan("CL-10002", dental, LoanProduct.CreMortgage, 180_000_000, 625, 120, 300, anchor, monthsAgo: 36, LoanStatus.Current),
            NewLoan("CL-10003", freight, LoanProduct.TermLoan, 62_000_000, 725, 60, 60, anchor, monthsAgo: 18, LoanStatus.Current),
            NewLoan("CL-10004", ember, LoanProduct.TermLoan, 28_000_000, 800, 84, 84, anchor, monthsAgo: 20, LoanStatus.PastDue, missedCount: 2, lateInstallment: 11),
            NewLoan("CL-10005", monongahela, LoanProduct.CreMortgage, 310_000_000, 690, 120, 300, anchor, monthsAgo: 30, LoanStatus.Current),
            NewLoan("CL-10006", ridgeline, LoanProduct.TermLoan, 38_000_000, 850, 48, 48, anchor, monthsAgo: 16, LoanStatus.PastDue, missedCount: 1, lateInstallment: 9),
            NewLoan("CL-10007", laurel, LoanProduct.TermLoan, 12_000_000, 700, 36, 36, anchor, monthsAgo: 40, LoanStatus.PaidOff),
            NewLoan("CL-10008", laurel, LoanProduct.TermLoan, 26_000_000, 750, 60, 60, anchor, monthsAgo: 12, LoanStatus.Current),
            NewLoan("CL-10009", dental, LoanProduct.TermLoan, 15_000_000, 710, 60, 60, anchor, monthsAgo: 15, LoanStatus.Current),
            NewLoan("CL-10010", freight, LoanProduct.CreMortgage, 95_000_000, 665, 120, 240, anchor, monthsAgo: 20, LoanStatus.Current));

        await db.SaveChangesAsync(cancellationToken);

        // Underwriting state (after save so application IDs exist).
        ApplyUnderwriting(a5, ironworks, underwriterId, RunAt(anchor.AddDays(-10), 14, 30));

        ApplyUnderwriting(a6, ridgeline, underwriterId, RunAt(anchor.AddDays(-30), 10, 15));
        RequireSuccess(a6.RecordDecision(DecisionOutcome.Decline, null, null, underwriterId, RunAt(anchor.AddDays(-28), 16, 0)));

        await db.SaveChangesAsync(cancellationToken);
    }

    private static Application NewApplication(
        Borrower borrower,
        string officerId,
        LoanProduct product,
        long amountCents,
        int rateBps,
        int termMonths,
        int amortizationMonths,
        string purpose,
        DateOnly submittedOn,
        long revenue,
        long noi,
        long existingDebtService,
        CollateralType collateralType,
        long appraisedValueCents) =>
        Require(Application.Create(
            borrower.Id,
            officerId,
            product,
            amountCents,
            rateBps,
            termMonths,
            amortizationMonths,
            purpose,
            submittedOn,
            Require(Financials.Create(revenue, noi, existingDebtService)),
            Require(Collateral.Create(collateralType, appraisedValueCents))));

    /// <summary>
    /// Builds a loan whose payment history is the amortization schedule up to the anchor.
    /// The last <paramref name="missedCount"/> installments are missed; <paramref name="lateInstallment"/> is paid 6 days late.
    /// </summary>
    private static Loan NewLoan(
        string loanNumber,
        Borrower borrower,
        LoanProduct product,
        long principalCents,
        int rateBps,
        int termMonths,
        int amortizationMonths,
        DateOnly anchor,
        int monthsAgo,
        LoanStatus status,
        int missedCount = 0,
        int? lateInstallment = null)
    {
        var origination = anchor.AddMonths(-monthsAgo);
        var loan = Require(Loan.Create(loanNumber, borrower.Id, null, product, principalCents, rateBps, termMonths, amortizationMonths, origination, status));

        var schedule = Require(AmortizationCalculator.Build(principalCents, rateBps, termMonths, amortizationMonths, origination.AddMonths(1)));
        var history = schedule.Rows.Where(r => r.DueDate <= anchor).ToList();

        for (var i = 0; i < history.Count; i++)
        {
            var row = history[i];
            var missed = i >= history.Count - missedCount;
            var late = row.Number == lateInstallment;

            DateOnly? paidDate = missed ? null : late ? row.DueDate.AddDays(6) : row.DueDate;
            var paymentStatus = missed ? PaymentStatus.Missed : late ? PaymentStatus.Late : PaymentStatus.OnTime;

            loan.AddPayment(Require(Payment.Create(row.DueDate, paidDate, row.PaymentCents, row.PrincipalCents, row.InterestCents, paymentStatus)));
        }

        return loan;
    }

    private static void ApplyUnderwriting(Application application, Borrower borrower, string runByUserId, DateTimeOffset runAt)
    {
        var outcome = UnderwritingEngine.Evaluate(application, borrower);
        var result = Require(UnderwritingResult.Create(
            outcome.DscrX100,
            outcome.LtvBps,
            outcome.RiskRating,
            outcome.Recommendation,
            outcome.Exceptions,
            outcome.ProposedMonthlyPaymentCents,
            runAt,
            runByUserId));

        RequireSuccess(application.ApplyUnderwriting(result));
    }

    private static DateTimeOffset RunAt(DateOnly date, int hour, int minute) =>
        new(date.ToDateTime(new TimeOnly(hour, minute)), TimeSpan.Zero);

    private static T Require<T>(Result<T> result) =>
        result.IsSuccess ? result.Value : throw SeedFailure(result.Error);

    private static void RequireSuccess(Result result)
    {
        if (result.IsFailure)
        {
            throw SeedFailure(result.Error);
        }
    }

    private static InvalidOperationException SeedFailure(Error error) =>
        new($"Seed data is invalid: {error.Code} — {error.Message}");

    private static void ThrowIfFailed(IdentityResult result, string action)
    {
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Seeding failed to {action}: {errors}");
        }
    }
}
