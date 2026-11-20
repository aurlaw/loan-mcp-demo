using Loans.Core.Common;

namespace Loans.Core.Domain;

/// <summary>Single collateral item (owned by <see cref="Application"/>).</summary>
public sealed record Collateral
{
    private Collateral() { }

    public CollateralType Type { get; private init; }
    public long AppraisedValueCents { get; private init; }

    public static Result<Collateral> Create(CollateralType type, long appraisedValueCents)
    {
        if (appraisedValueCents <= 0)
        {
            return Result<Collateral>.Failure(new Error("collateral.invalid_value", "Appraised value must be greater than zero."));
        }

        return Result<Collateral>.Success(new Collateral { Type = type, AppraisedValueCents = appraisedValueCents });
    }
}
