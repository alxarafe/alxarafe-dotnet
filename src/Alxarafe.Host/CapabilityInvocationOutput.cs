using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Alxarafe.AspNetCore;

namespace Alxarafe.Host;

internal static class CapabilityInvocationOutput
{
    private const string UomCollection = "#/components/schemas/WmsUomCollection";
    private const string StorageAttributeCollection = "#/components/schemas/WmsStorageAttributeCollection";
    private const string ItemFamilyCollection = "#/components/schemas/WmsItemFamilyCollection";
    private const string ItemsField = "items";
    private const string OffsetField = "offset";
    private const string LimitField = "limit";
    private const string TotalField = "total";
    private const string OrderField = "order";
    private const string FieldField = "field";
    private const string DirectionField = "direction";
    private const string IdField = "id";
    private const string CodeField = "code";
    private const string DescriptionField = "description";
    private const string NameField = "name";
    private const string Ascending = "asc";
    private const int UomCodeMaximum = 10;
    private const int CatalogueCodeMaximum = 30;
    private const int LabelMaximum = 255;
    private static readonly string[] Fields = [ItemsField, LimitField, OffsetField, OrderField, TotalField];
    private static readonly string[] UomFields = [CodeField, DescriptionField, IdField];
    private static readonly string[] CatalogueFields = [CodeField, IdField, NameField];

    public static JsonElement? Normalize(string schemaReference, object? value)
    {
        if (value is null || schemaReference is not (UomCollection or StorageAttributeCollection or ItemFamilyCollection)) return null;
        using var document = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(value, JsonSerializerOptions.Web));
        var root = document.RootElement;
        return Valid(root, schemaReference) ? root.Clone() : null;
    }

    private static bool Valid(JsonElement root, string schemaReference)
    {
        if (root.ValueKind != JsonValueKind.Object) return false;
        string[] names = root.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();
        if (!names.SequenceEqual(Fields)) return false;
        if (!NonNegativeInteger(root.GetProperty(OffsetField)) || !NonNegativeInteger(root.GetProperty(TotalField))) return false;
        if (!root.GetProperty(LimitField).TryGetInt32(out int limit)
            || limit < CollectionPagination.MinimumLimit || limit > CollectionPagination.MaximumLimit) return false;
        var items = root.GetProperty(ItemsField);
        if (items.ValueKind != JsonValueKind.Array) return false;
        bool uom = schemaReference == UomCollection;
        foreach (var item in items.EnumerateArray())
            if (!ValidItem(item, uom)) return false;
        var order = root.GetProperty(OrderField);
        if (order.ValueKind != JsonValueKind.Array || order.GetArrayLength() != 1) return false;
        var entry = order[0];
        if (entry.ValueKind != JsonValueKind.Object) return false;
        var properties = entry.EnumerateObject().ToArray();
        return properties.Length == 2
            && properties[0].Name == FieldField && properties[0].Value.ValueKind == JsonValueKind.String
            && properties[0].Value.GetString() == CodeField
            && properties[1].Name == DirectionField && properties[1].Value.ValueKind == JsonValueKind.String
            && properties[1].Value.GetString() == Ascending;
    }

    private static bool ValidItem(JsonElement item, bool uom)
    {
        if (item.ValueKind != JsonValueKind.Object) return false;
        string[] expected = uom ? UomFields : CatalogueFields;
        string[] names = item.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();
        if (!names.SequenceEqual(expected)) return false;
        if (!EntityId(item.GetProperty(IdField)) || !Code(item.GetProperty(CodeField), uom)) return false;
        return Label(item.GetProperty(uom ? DescriptionField : NameField));
    }

    private static bool EntityId(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String) return false;
        string? text = value.GetString();
        return text is not null && Guid.TryParse(text, out var id) && id != Guid.Empty && id.ToString() == text;
    }

    private static bool Code(JsonElement value, bool uom)
    {
        if (value.ValueKind != JsonValueKind.String) return false;
        string? text = value.GetString();
        if (string.IsNullOrEmpty(text) || text.Length > (uom ? UomCodeMaximum : CatalogueCodeMaximum)
            || text[0] is < 'A' or > 'Z') return false;
        return text.Skip(1).All(character => character is >= 'A' and <= 'Z' or >= '0' and <= '9' || (!uom && character == '_'));
    }

    private static bool Label(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String) return false;
        string? text = value.GetString();
        if (string.IsNullOrEmpty(text) || text[0] is ' ' or '\t' or '\r' or '\n'
            || text[^1] is ' ' or '\t' or '\r' or '\n') return false;
        int scalars = 0;
        for (int index = 0; index < text.Length; index++)
        {
            char character = text[index];
            if (character == '\0') return false;
            if (char.IsHighSurrogate(character))
            {
                if (index + 1 >= text.Length || !char.IsLowSurrogate(text[index + 1])) return false;
                index++;
            }
            else if (char.IsLowSurrogate(character)) return false;
            if (++scalars > LabelMaximum) return false;
        }
        return true;
    }

    private static bool NonNegativeInteger(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number) return false;
        string text = value.GetRawText();
        return text.Length > 0 && text.All(character => character is >= '0' and <= '9')
            && BigInteger.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out _);
    }
}
