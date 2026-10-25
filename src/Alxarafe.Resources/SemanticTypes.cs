using System.Numerics;

namespace Alxarafe.Resources;

#pragma warning disable CA1720 // Semantic catalog names deliberately describe domains, not CLR storage mappings.
public enum ScalarType
{
    Boolean, String, Text, Int8, UInt8, Int16, UInt16, Int32, UInt32, Int64,
    Decimal, Uuid, Date, Time, DateTime, Enum, Binary
}

#pragma warning restore CA1720

public sealed record NumericDomain(ResourceNumber? Minimum, ResourceNumber? Maximum, ResourceNumber? Step)
{
    public static NumericDomain? Intrinsic(ScalarType type) => type switch
    {
        ScalarType.Int8 => Integer(sbyte.MinValue, sbyte.MaxValue),
        ScalarType.UInt8 => Integer(byte.MinValue, byte.MaxValue),
        ScalarType.Int16 => Integer(short.MinValue, short.MaxValue),
        ScalarType.UInt16 => Integer(ushort.MinValue, ushort.MaxValue),
        ScalarType.Int32 => Integer(int.MinValue, int.MaxValue),
        ScalarType.UInt32 => Integer(uint.MinValue, uint.MaxValue),
        ScalarType.Int64 => Integer(long.MinValue, long.MaxValue),
        ScalarType.Decimal => new(null, null, null),
        _ => null
    };

    private static NumericDomain Integer(BigInteger minimum, BigInteger maximum) =>
        new(new ResourceNumber(minimum), new ResourceNumber(maximum), ResourceNumber.One);
}

public abstract record FieldConstraints;
public sealed record NumericConstraints(ResourceNumber? Minimum = null, ResourceNumber? Maximum = null,
    bool ExclusiveMinimum = false, bool ExclusiveMaximum = false, ResourceNumber? MultipleOf = null) : FieldConstraints;
public sealed record TextConstraints(int? MinimumLength = null, int? MaximumLength = null, string? Pattern = null) : FieldConstraints;
public sealed record DecimalConstraints(int Precision, int Scale, NumericConstraints? Range = null) : FieldConstraints;

public abstract record ScalarValue;
public sealed record NullValue : ScalarValue;
public sealed record BooleanValue(bool Value) : ScalarValue;
public sealed record TextValue(string Value) : ScalarValue;
public sealed record NumericValue(ResourceNumber Value) : ScalarValue;
public sealed record UuidValue(Guid Value) : ScalarValue;
public sealed record DateValue(DateOnly Value) : ScalarValue;
public sealed record TimeValue(TimeOnly Value) : ScalarValue;
// DateTime is an instant; offsets are canonicalized to UTC, never a server timezone.
public sealed record DateTimeValue(DateTimeOffset Value) : ScalarValue;
// Immutable base64 representation, validated and normalized before canonical hashing.
public sealed record BinaryValue(string Base64) : ScalarValue;
