using System.Globalization;
using Microsoft.AspNetCore.Http;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alxarafe.AspNetCore;

// The existing COLLECTIONS-001 query policy, shared by Users and Modules.
public sealed record CollectionPagination(BigInteger Offset, int Limit)
{
    public const string OffsetParameter = "offset";
    public const string LimitParameter = "limit";
    public const int MinimumLimit = 1;
    public const int DefaultLimit = 50;
    public const int MaximumLimit = 100;
    public static IReadOnlyList<CollectionOrder> IdAscending { get; } = Array.AsReadOnly(new CollectionOrder[] { new("id", "asc") });

    public static CollectionPagination? Read(IQueryCollection query)
    {
        var offsetText = query.ContainsKey(OffsetParameter) ? query[OffsetParameter].ToString() : BigInteger.Zero.ToString(CultureInfo.InvariantCulture);
        var limitText = query.ContainsKey(LimitParameter) ? query[LimitParameter].ToString() : DefaultLimit.ToString(CultureInfo.InvariantCulture);
        return Integer(offsetText, out var offset) && Integer(limitText, out var limit) && limit >= MinimumLimit && limit <= MaximumLimit
            ? new(offset, (int)limit) : null;
    }

    private static bool Integer(string text, out BigInteger value)
    {
        value = BigInteger.Zero;
        return text.Length > 0 && text.All(character => character is >= '0' and <= '9')
            && BigInteger.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CollectionOrder(string Field, string Direction);

public sealed class OffsetJsonConverter : JsonConverter<BigInteger>
{
    public override BigInteger Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("Offset is response metadata only.");
    public override void Write(Utf8JsonWriter writer, BigInteger value, JsonSerializerOptions options) =>
        writer.WriteRawValue(value.ToString(CultureInfo.InvariantCulture));
}
