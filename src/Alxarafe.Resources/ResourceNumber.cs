using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace Alxarafe.Resources;

// Exact base-ten metadata number. No CLR decimal precision or floating-point rounding.
public sealed record ResourceNumber : IComparable<ResourceNumber>
{
    internal const int Radix = 10;
    private static readonly Regex Syntax = new(@"\A-?[0-9]+(?:\.[0-9]+)?\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    public BigInteger Coefficient { get; }
    public int Scale { get; }
    public bool IsInteger => Scale == 0;
    public static ResourceNumber Zero { get; } = new(BigInteger.Zero, 0);
    public static ResourceNumber One { get; } = new(BigInteger.One, 0);

    public ResourceNumber(BigInteger coefficient, int scale = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(scale);
        if (coefficient.IsZero) scale = 0;
        while (scale > 0 && coefficient % Radix == 0) { coefficient /= Radix; scale--; }
        Coefficient = coefficient;
        Scale = scale;
    }

    public static ResourceNumber Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!Syntax.IsMatch(value)) throw new FormatException("Expected a plain base-ten metadata number.");
        int point = value.IndexOf('.', StringComparison.Ordinal);
        return new(BigInteger.Parse(value.Replace(".", "", StringComparison.Ordinal), CultureInfo.InvariantCulture),
            point < 0 ? 0 : value.Length - point - 1);
    }

    public int CompareTo(ResourceNumber? other)
    {
        if (other is null) return 1;
        int scale = Math.Max(Scale, other.Scale);
        return AtScale(scale).CompareTo(other.AtScale(scale));
    }

    public static bool operator <(ResourceNumber left, ResourceNumber right) => left.CompareTo(right) < 0;
    public static bool operator <=(ResourceNumber left, ResourceNumber right) => left.CompareTo(right) <= 0;
    public static bool operator >(ResourceNumber left, ResourceNumber right) => left.CompareTo(right) > 0;
    public static bool operator >=(ResourceNumber left, ResourceNumber right) => left.CompareTo(right) >= 0;

    internal BigInteger AtScale(int scale) => Coefficient * BigInteger.Pow(Radix, scale - Scale);
    internal bool IsMultipleOf(ResourceNumber step)
    {
        int scale = Math.Max(Scale, step.Scale);
        return AtScale(scale) % step.AtScale(scale) == 0;
    }

    internal BigInteger FloorQuotient(ResourceNumber step)
    {
        int scale = Math.Max(Scale, step.Scale);
        var quotient = BigInteger.DivRem(AtScale(scale), step.AtScale(scale), out var remainder);
        return remainder.Sign < 0 ? quotient - BigInteger.One : quotient;
    }

    internal static ResourceNumber CommonStep(ResourceNumber first, ResourceNumber second)
    {
        int scale = Math.Max(first.Scale, second.Scale);
        var a = first.AtScale(scale);
        var b = second.AtScale(scale);
        return new(a / BigInteger.GreatestCommonDivisor(a, b) * b, scale);
    }

    public string CanonicalText
    {
        get
        {
            string digits = BigInteger.Abs(Coefficient).ToString(CultureInfo.InvariantCulture);
            if (Scale > 0)
            {
                digits = digits.PadLeft(Scale + 1, '0');
                digits = digits.Insert(digits.Length - Scale, ".");
            }
            return Coefficient.Sign < 0 ? "-" + digits : digits;
        }
    }

    public override string ToString() => CanonicalText;
}
