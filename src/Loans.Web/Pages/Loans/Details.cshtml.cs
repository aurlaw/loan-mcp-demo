using Loans.Core.Data.Queries;
using Loans.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Loans.Web.Pages.Loans;

public class DetailsModel(ICurrentUserScope currentUserScope, ILoanQueries loans, TimeProvider timeProvider) : PageModel
{
    public LoanDetail Loan { get; private set; } = null!;
    public LoanSchedule Schedule { get; private set; } = null!;

    [BindProperty(SupportsGet = true)]
    public DateOnly? PayoffDate { get; set; }

    public PayoffQuoteDto? Quote { get; private set; }
    public string? QuoteError { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var scope = (await currentUserScope.GetAsync(cancellationToken)).QueryScope;

        var loan = await loans.GetDetailAsync(id, scope, cancellationToken);
        var schedule = await loans.GetScheduleAsync(id, scope, cancellationToken);
        if (loan is null || schedule is null)
        {
            return NotFound();
        }

        Loan = loan;
        Schedule = schedule;

        PayoffDate ??= DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var quote = await loans.GetPayoffQuoteAsync(id, PayoffDate.Value, scope, cancellationToken);

        if (quote.IsSuccess)
        {
            Quote = quote.Value;
        }
        else if (quote.Error.Code == QueryErrors.LoanNotFound)
        {
            return NotFound();
        }
        else
        {
            QuoteError = quote.Error.Code switch
            {
                "payoff.date_before_paid_through" =>
                    $"The payoff date must be on or after the paid-through date ({Formatting.Date(loan.PaidThroughDate)}).",
                "payoff.loan_paid_off" => "This loan is paid off — there is no payoff amount due.",
                _ => "A payoff quote couldn't be calculated for this date.",
            };
        }

        return Page();
    }
}
