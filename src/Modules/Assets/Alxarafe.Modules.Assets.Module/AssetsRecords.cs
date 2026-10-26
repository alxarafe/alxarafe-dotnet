using System.Globalization;
using Alxarafe.Resources;
using Alxarafe.Resources.Runtime;

namespace Alxarafe.Modules.Assets;

internal static class AssetsRecords
{
    internal static ResourceRecord Catalogue(AssetCatalogueRow row)
    {
        var values = new Dictionary<string, ScalarValue>(StringComparer.Ordinal)
        {
            [AssetFields.Id] = new UuidValue(row.Id), [AssetFields.Code] = new TextValue(row.Code),
            [AssetFields.Name] = new TextValue(row.Name), [AssetFields.Active] = new BooleanValue(row.Active),
            [AssetFields.CreatedAt] = new DateTimeValue(row.CreatedAt), [AssetFields.UpdatedAt] = new DateTimeValue(row.UpdatedAt)
        };
        if (row is AssetTypeRow type) values.Add(AssetFields.Description, Text(type.Description));
        if (row is AssetModelRow model) values.Add(AssetFields.ManufacturerId, new UuidValue(model.ManufacturerId));
        return new(values);
    }
    internal static ResourceRecord Asset(AssetRow row) => new(new Dictionary<string, ScalarValue>(StringComparer.Ordinal)
    {
        [AssetFields.Id] = new UuidValue(row.Id), [AssetFields.Name] = new TextValue(row.Name), [AssetFields.AssetTypeId] = new UuidValue(row.AssetTypeId),
        [AssetFields.AssetTag] = Text(row.AssetTag), [AssetFields.SerialNumber] = Text(row.SerialNumber),
        [AssetFields.ManufacturerId] = Uuid(row.ManufacturerId), [AssetFields.AssetModelId] = Uuid(row.AssetModelId), [AssetFields.LocationId] = Uuid(row.LocationId),
        [AssetFields.StatusId] = new UuidValue(row.StatusId), [AssetFields.Lifecycle] = new TextValue(row.Lifecycle),
        [AssetFields.PurchaseDate] = row.PurchaseDate is { } date ? new DateValue(date) : new NullValue(),
        [AssetFields.PurchasePrice] = row.PurchasePrice is { } price ? Number(price) : new NullValue(),
        [AssetFields.Condition] = row.Condition is { } condition ? Number(condition) : new NullValue(), [AssetFields.Notes] = Text(row.Notes),
        [AssetFields.CreatedAt] = new DateTimeValue(row.CreatedAt), [AssetFields.UpdatedAt] = new DateTimeValue(row.UpdatedAt)
    });
    private static NumericValue Number(decimal value) => new NumericValue(ResourceNumber.Parse(value.ToString(CultureInfo.InvariantCulture)));
    private static ScalarValue Text(string? value) => value is null ? new NullValue() : new TextValue(value);
    private static ScalarValue Uuid(Guid? value) => value is null ? new NullValue() : new UuidValue(value.Value);

    internal static void Patch(AssetRow row, ResourceRecord values)
    {
        foreach (var (field, value) in values.Values)
        {
            switch (field)
            {
                case AssetFields.Name: row.Name = ((TextValue)value).Value; break;
                case AssetFields.AssetTag: row.AssetTag = OptionalText(value); break;
                case AssetFields.SerialNumber: row.SerialNumber = OptionalText(value); break;
                case AssetFields.ManufacturerId: row.ManufacturerId = OptionalUuid(value); break;
                case AssetFields.AssetModelId: row.AssetModelId = OptionalUuid(value); break;
                case AssetFields.LocationId: row.LocationId = OptionalUuid(value); break;
                case AssetFields.PurchaseDate: row.PurchaseDate = value is DateValue date ? date.Value : null; break;
                case AssetFields.PurchasePrice: row.PurchasePrice = value is NumericValue price ? decimal.Parse(price.Value.CanonicalText, CultureInfo.InvariantCulture) : null; break;
                case AssetFields.Condition: row.Condition = value is NumericValue condition ? byte.Parse(condition.Value.CanonicalText, CultureInfo.InvariantCulture) : null; break;
                case AssetFields.Notes: row.Notes = OptionalText(value); break;
            }
        }
    }
    internal static string? OptionalText(ScalarValue value) => value is TextValue text ? text.Value : null;
    internal static Guid? OptionalUuid(ScalarValue value) => value is UuidValue uuid ? uuid.Value : null;
}
