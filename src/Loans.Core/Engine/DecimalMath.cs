namespace Loans.Core.Engine;

internal static class DecimalMath
{
    /// <summary>Integer power by squaring; avoids <see cref="Math.Pow"/> (double).</summary>
    public static decimal Pow(decimal value, int exponent)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(exponent);

        var result = 1m;
        while (exponent > 0)
        {
            if ((exponent & 1) == 1)
            {
                result *= value;
            }

            value *= value;
            exponent >>= 1;
        }

        return result;
    }

    /// <summary>Rounds a cents amount to a whole cent, midpoint away from zero.</summary>
    public static long RoundCents(decimal cents) =>
        (long)decimal.Round(cents, 0, MidpointRounding.AwayFromZero);
}
