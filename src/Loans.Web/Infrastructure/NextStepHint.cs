using Loans.Core.Domain;
using Loans.Core.Security;

namespace Loans.Web.Infrastructure;

/// <summary>
/// Orienting text on application detail. Wording depends on what the viewer can do:
/// decision-makers (Underwriter) vs. underwriting-runners (Loan Officer); everyone else sees status only.
/// </summary>
public static class NextStepHint
{
    public static string For(ApplicationStatus status, IReadOnlySet<Capability> capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        var decides = capabilities.Contains(Capability.DecisionsWrite);
        var runsUnderwriting = capabilities.Contains(Capability.UnderwritingRun);

        if (!decides && !runsUnderwriting)
        {
            return $"Status: {Formatting.Humanize(status)}.";
        }

        return status switch
        {
            ApplicationStatus.Submitted => decides ? "Not yet underwritten." : "Ready for underwriting analysis.",
            ApplicationStatus.InUnderwriting => decides ? "Awaiting your credit decision." : "With underwriting — awaiting decision.",
            ApplicationStatus.Approved or ApplicationStatus.Declined => "Decision recorded.",
            _ => $"Status: {Formatting.Humanize(status)}.",
        };
    }
}
