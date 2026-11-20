using Loans.Core.Domain;

namespace Loans.Core.Engine;

public static class UnderwritingPolicy
{
    public const int MinDscrX100 = 125;
    public const int MinYearsInBusiness = 2;

    /// <summary>Maximum LTV in basis points, or null when LTV is not evaluated (blanket lien).</summary>
    public static int? MaxLtvBps(CollateralType type) => type switch
    {
        CollateralType.RealEstate => 7500,
        CollateralType.Equipment => 8000,
        CollateralType.BlanketLien => null,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };
}
