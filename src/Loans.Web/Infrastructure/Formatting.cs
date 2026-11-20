using System.Globalization;
using System.Text;
using Loans.Core.Domain;

namespace Loans.Web.Infrastructure;

/// <summary>Display formatting for cents, basis points, ratios, dates and enums.</summary>
public static class Formatting
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-US");

    private static readonly Dictionary<Enum, string> EnumOverrides = new()
    {
        [LoanProduct.CreMortgage] = "CRE mortgage",
        [EntityType.Llc] = "LLC",
        [PolicyException.DscrBelowMinimum] = "DSCR below minimum",
        [PolicyException.LtvAboveMaximum] = "LTV above maximum",
    };

    public static string Money(long cents) => (cents / 100m).ToString("C2", Culture);

    public static string Rate(int bps) => (bps / 100m).ToString("0.00", Culture) + "%";

    public static string Rate(int? bps) => bps is { } value ? Rate(value) : "—";

    public static string Dscr(int dscrX100) => (dscrX100 / 100m).ToString("0.00", Culture) + "x";

    public static string Ltv(int? ltvBps) => ltvBps is { } value ? (value / 100m).ToString("0.00", Culture) + "%" : "n/a";

    public static string Date(DateOnly date) => date.ToString("MMM d, yyyy", Culture);

    public static string Date(DateOnly? date) => date is { } value ? Date(value) : "—";

    public static string DateTime(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("MMM d, yyyy h:mm tt", Culture) + " UTC";

    /// <summary>Months as "10 yrs" / "60 mo".</summary>
    public static string Months(int months) =>
        months % 12 == 0 ? $"{months / 12} yrs ({months} mo)" : $"{months} mo";

    /// <summary>PascalCase enum name → sentence case ("ApproveWithConditions" → "Approve with conditions").</summary>
    public static string Humanize(Enum value)
    {
        if (EnumOverrides.TryGetValue(value, out var label))
        {
            return label;
        }

        return Humanize(value.ToString());
    }

    public static string Humanize(string pascalCase)
    {
        var sb = new StringBuilder(pascalCase.Length + 8);
        for (var i = 0; i < pascalCase.Length; i++)
        {
            var c = pascalCase[i];
            if (i > 0 && char.IsUpper(c))
            {
                sb.Append(' ').Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    /// <summary>Bootstrap badge classes: green = good, red = bad, amber = in progress/attention, gray = neutral.</summary>
    public static string BadgeClass(Enum value) => value switch
    {
        LoanStatus.Current or ApplicationStatus.Approved or PaymentStatus.OnTime
            or Recommendation.Approve or DecisionOutcome.Approve => "text-bg-success",
        LoanStatus.PastDue or ApplicationStatus.Declined or PaymentStatus.Missed
            or Recommendation.Decline or DecisionOutcome.Decline => "text-bg-danger",
        ApplicationStatus.InUnderwriting or PaymentStatus.Late
            or Recommendation.ApproveWithConditions or DecisionOutcome.ApproveWithConditions => "text-bg-warning",
        _ => "text-bg-secondary",
    };
}
