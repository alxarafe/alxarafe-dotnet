using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Alxarafe.Resources.Runtime;
using Microsoft.AspNetCore.Http;

namespace Alxarafe.Resources.AspNetCore;

internal sealed class ResourceTransportException(string path) : Exception
{
    internal string Path { get; } = path;
}

public static class ResourceTransport
{
    public const int MaximumBodyBytes = 65_536;
    public const int MaximumNumericDigits = 4096;
    private const int NumericRadix = 10;
    private const int MaximumBodyDepth = 32;
    private const int MaximumFilterDepth = 4;
    private const string DateFormat = "yyyy-MM-dd";
    private const string TimeFormat = "HH:mm:ss.FFFFFFF";
    private const string InstantFormat = "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK";
    internal static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web);

    public static ScalarValue Parse(ScalarType type, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null) return new NullValue();
        string? text = value.ValueKind == JsonValueKind.String ? ReadString(value) : null;
        return type switch
        {
            ScalarType.Boolean when value.ValueKind is JsonValueKind.True or JsonValueKind.False => new BooleanValue(value.GetBoolean()),
            ScalarType.String or ScalarType.Text or ScalarType.Enum when text is not null => new TextValue(text),
            ScalarType.Int8 or ScalarType.UInt8 or ScalarType.Int16 or ScalarType.UInt16 or ScalarType.Int32 or ScalarType.UInt32 or ScalarType.Int64 or ScalarType.Decimal
                when value.ValueKind == JsonValueKind.Number => new NumericValue(Number(value.GetRawText())),
            ScalarType.Uuid when text is not null && Guid.TryParseExact(text, "D", out var uuid) => new UuidValue(uuid),
            ScalarType.Date when DateOnly.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) => new DateValue(date),
            ScalarType.Time when TimeOnly.TryParseExact(text, ["HH:mm:ss", TimeFormat], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) => new TimeValue(time),
            ScalarType.DateTime when text is not null && ExplicitOffset(text) && DateTimeOffset.TryParseExact(text, ["yyyy-MM-dd'T'HH:mm:ssK", InstantFormat], CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant) => new DateTimeValue(instant),
            ScalarType.Binary when text is not null && Base64(text) => new BinaryValue(text),
            _ => throw new ResourceTransportException("value")
        };
    }

    private static string ReadString(JsonElement value)
    {
        try { return value.GetString()!; }
        catch (InvalidOperationException) { throw new ResourceTransportException("value"); }
    }

    private static JsonDocument ParseDocument(ReadOnlyMemory<byte> bytes, int maximumDepth)
    {
        // JsonDocument can retain escaped lone surrogates until string decoding.
        // Reject invalid text in values AND property names at the bounded transport
        // boundary; do not mistake decoder failures for server/adapter failures.
        var reader = new Utf8JsonReader(bytes.Span, new JsonReaderOptions { MaxDepth = maximumDepth });
        while (reader.Read())
            if (reader.TokenType is JsonTokenType.String or JsonTokenType.PropertyName)
            {
                try { _ = reader.GetString(); }
                catch (InvalidOperationException) { throw new ResourceTransportException("body"); }
            }
        return JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = maximumDepth });
    }

    private static bool ExplicitOffset(string value) => value.EndsWith('Z') || (value.Length >= 6 && value[^3] == ':' && value[^6] is '+' or '-');
    private static bool Base64(string value)
    {
        try { Convert.FromBase64String(value); return true; } catch (FormatException) { return false; }
    }
    private static ResourceNumber Number(string raw)
    {
        int marker = raw.IndexOfAny(['e', 'E']);
        string mantissa = marker < 0 ? raw : raw[..marker];
        if (mantissa.Count(char.IsDigit) > MaximumNumericDigits) throw new ResourceTransportException("value");
        var number = ResourceNumber.Parse(mantissa);
        int exponent = 0;
        if (marker >= 0 && (!int.TryParse(raw[(marker + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent)
            || exponent < -MaximumNumericDigits || exponent > MaximumNumericDigits)) throw new ResourceTransportException("value");
        int scale = number.Scale - exponent;
        if (Math.Abs(scale) > MaximumNumericDigits || mantissa.Count(char.IsDigit) + Math.Max(-scale, 0) > MaximumNumericDigits)
            throw new ResourceTransportException("value");
        return scale >= 0 ? new(number.Coefficient, scale) : new(number.Coefficient * BigInteger.Pow(NumericRadix, -scale));
    }

    public static JsonElement Project(ScalarValue value) => value switch
    {
        NullValue => Element("null"),
        BooleanValue boolean => JsonSerializer.SerializeToElement(boolean.Value),
        TextValue text => JsonSerializer.SerializeToElement(text.Value),
        NumericValue number => Element(number.Value.CanonicalText),
        UuidValue uuid => JsonSerializer.SerializeToElement(uuid.Value.ToString("D", CultureInfo.InvariantCulture)),
        DateValue date => JsonSerializer.SerializeToElement(date.Value.ToString(DateFormat, CultureInfo.InvariantCulture)),
        TimeValue time => JsonSerializer.SerializeToElement(time.Value.ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture)),
        DateTimeValue instant => JsonSerializer.SerializeToElement(instant.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)),
        BinaryValue binary => JsonSerializer.SerializeToElement(Convert.ToBase64String(Convert.FromBase64String(binary.Base64))),
        _ => throw new InvalidOperationException("Unsupported semantic value projection.")
    };
    private static JsonElement Element(string json) { using var document = JsonDocument.Parse(json); return document.RootElement.Clone(); }
    internal static ResourceRecordDto Record(ResourceRecord record) => new() { Fields = record.Values.ToDictionary(pair => pair.Key, pair => Project(pair.Value), StringComparer.Ordinal) };

    internal static async Task<ResourceRecord> ReadAsync(HttpRequest request, ResourceDefinition definition, CancellationToken ct)
    {
        using var document = await ReadDocumentAsync(request, ct);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new ResourceTransportException("body");
        var values = new Dictionary<string, ScalarValue>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (values.ContainsKey(property.Name)) throw new ResourceTransportException(property.Name);
            var field = definition.Fields.SingleOrDefault(field => field.Name == property.Name);
            try
            {
                // Unknown keys carry no interpreted value: Runtime rejects them under
                // the normal guards. No untyped object enters the execution boundary.
                values.Add(property.Name, field is null ? new NullValue() : Parse(field.Type, property.Value));
            }
            catch (ResourceTransportException) { throw new ResourceTransportException(property.Name); }
        }
        return new(values);
    }

    internal static async Task<JsonDocument> ReadDocumentAsync(HttpRequest request, CancellationToken ct)
    {
        if (!request.HasJsonContentType() || request.ContentLength > MaximumBodyBytes) throw new ResourceTransportException("body");
        var bytes = new byte[MaximumBodyBytes + 1];
        int count = await request.Body.ReadAtLeastAsync(bytes, bytes.Length, throwOnEndOfStream: false, ct);
        if (count > MaximumBodyBytes) throw new ResourceTransportException("body");
        return ParseDocument(bytes.AsMemory(0, count), MaximumBodyDepth);
    }

    internal static ResourceIdentifier Identifier(ResourceDefinition definition, string id)
    {
        var field = definition.Fields.Single(field => field.Name == definition.IdDefinition.Field);
        try
        {
            using var value = JsonDocument.Parse(field.Type == ScalarType.String ? JsonSerializer.Serialize(id) : field.Type == ScalarType.Uuid ? JsonSerializer.Serialize(id) : id);
            return new(field.Name, Parse(field.Type, value.RootElement));
        }
        catch (Exception failure) when (failure is JsonException or ResourceTransportException or FormatException) { throw new ResourceTransportException("id"); }
    }

    internal static ResourceQuery Query(HttpRequest request, ResourceDefinition definition)
    {
        long offset = 0; int limit = ResourceQuery.DefaultLimit;
        var filters = new List<ResourceFilter>(); var sorting = new List<ResourceSort>(); string? search = null;
        foreach (var pair in request.Query)
        {
            if (pair.Key is "offset" or "limit" or "search" && pair.Value.Count != 1) throw new ResourceTransportException(pair.Key);
            switch (pair.Key)
            {
                case "offset": if (!long.TryParse(pair.Value[0], NumberStyles.None, CultureInfo.InvariantCulture, out offset)) throw new ResourceTransportException(pair.Key); break;
                case "limit": if (!int.TryParse(pair.Value[0], NumberStyles.None, CultureInfo.InvariantCulture, out limit)) throw new ResourceTransportException(pair.Key); break;
                case "search": search = pair.Value[0]; break;
                case "sort":
                    foreach (var item in pair.Value)
                    {
                        if (string.IsNullOrEmpty(item)) throw new ResourceTransportException(pair.Key);
                        sorting.Add(new(item[0] == '-' ? item[1..] : item, item[0] == '-' ? ResourceSortDirection.Descending : ResourceSortDirection.Ascending));
                    }
                    break;
                case "filter":
                    foreach (var item in pair.Value)
                    {
                        if (item is null || item.Length > MaximumBodyBytes) throw new ResourceTransportException(pair.Key);
                        using var document = ParseDocument(Encoding.UTF8.GetBytes(item), MaximumFilterDepth);
                        var root = document.RootElement;
                        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 3
                            || !root.TryGetProperty("field", out var name) || name.ValueKind != JsonValueKind.String
                            || !root.TryGetProperty("operator", out var op) || op.ValueKind != JsonValueKind.String
                            || !root.TryGetProperty("value", out var value)) throw new ResourceTransportException(pair.Key);
                        var fieldName = ReadString(name);
                        var field = definition.Fields.SingleOrDefault(field => field.Name == fieldName);
                        filters.Add(new(fieldName, Operator(ReadString(op)), field is null ? new NullValue() : Parse(field.Type, value)));
                    }
                    break;
                default: throw new ResourceTransportException(pair.Key);
            }
        }
        return new(offset, limit, filters, sorting, search);
    }
    internal static string OperatorName(ResourceFilterOperator value) => value switch
    {
        ResourceFilterOperator.Equal => "eq",
        ResourceFilterOperator.NotEqual => "ne",
        ResourceFilterOperator.LessThan => "lt",
        ResourceFilterOperator.LessThanOrEqual => "lte",
        ResourceFilterOperator.GreaterThan => "gt",
        ResourceFilterOperator.GreaterThanOrEqual => "gte",
        ResourceFilterOperator.Contains => "contains",
        ResourceFilterOperator.StartsWith => "startsWith",
        _ => throw new InvalidOperationException("Invalid filter operator.")
    };
    private static ResourceFilterOperator Operator(string value)
    {
        foreach (var operation in Enum.GetValues<ResourceFilterOperator>())
            if (OperatorName(operation) == value) return operation;
        throw new ResourceTransportException("filter.operator");
    }
}

public sealed class ResourceRecordDto
{
    [JsonExtensionData]
    public IDictionary<string, JsonElement> Fields { get; init; } = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
}
public sealed record ResourcePageDto(IReadOnlyList<ResourceRecordDto> Items, long Offset, int Limit,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? Total);
