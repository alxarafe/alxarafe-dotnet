using Alxarafe.Resources;
using Alxarafe.Resources.Runtime;

namespace Alxarafe.Modules.Assets;

internal static class AssetDynamicRecords
{
    internal static ResourceRecord Definition(AssetFieldDefinitionRow row) => new(new Dictionary<string, ScalarValue>(StringComparer.Ordinal)
    {
        [AssetFields.Id] = new UuidValue(row.Id), [AssetFields.AssetTypeId] = new UuidValue(row.AssetTypeId),
        [AssetFields.Code] = new TextValue(row.Code), [AssetFields.Name] = new TextValue(row.Name),
        [AssetFields.Description] = row.Description is null ? new NullValue() : new TextValue(row.Description),
        [AssetFields.FieldType] = new TextValue(AssetDynamicSchema.TypeName(row.FieldType)),
        [AssetFields.Required] = new BooleanValue(row.Required), [AssetFields.Active] = new BooleanValue(row.Active),
        [AssetFields.DisplayOrder] = new NumericValue(new ResourceNumber(row.DisplayOrder)),
        [AssetFields.MinimumLength] = Integer(row.MinimumLength), [AssetFields.MaximumLength] = Integer(row.MaximumLength),
        [AssetFields.Minimum] = Number(row.Minimum), [AssetFields.Maximum] = Number(row.Maximum),
        [AssetFields.Precision] = Integer(row.Precision), [AssetFields.Scale] = Integer(row.Scale),
        [AssetFields.ValueField] = new TextValue(AssetDynamicSchema.Slot(row.FieldType)),
        [AssetFields.CreatedAt] = new DateTimeValue(row.CreatedAt), [AssetFields.UpdatedAt] = new DateTimeValue(row.UpdatedAt)
    });
    internal static ResourceRecord Value(AssetFieldValueRow row)
    {
        if (!AssetDynamicSchema.TryDecode(row.Definition.FieldType, row.ValueJson, out var value)
            || value is NullValue || !ResourceValueValidator.IsValid(AssetDynamicSchema.Field(row.Definition), value))
            throw new ResourceRuntimeException(AssetResources.FieldValue, ResourceRuntimeError.InvalidRepositoryResult);
        return new(new Dictionary<string, ScalarValue>(StringComparer.Ordinal)
        {
            [AssetFields.Id] = new UuidValue(row.Id), [AssetFields.AssetId] = new UuidValue(row.AssetId),
            [AssetFields.FieldDefinitionId] = new UuidValue(row.FieldDefinitionId), [AssetFields.AssetTypeId] = new UuidValue(row.AssetTypeId),
            [AssetDynamicSchema.Slot(row.Definition.FieldType)] = value,
            [AssetFields.CreatedAt] = new DateTimeValue(row.CreatedAt), [AssetFields.UpdatedAt] = new DateTimeValue(row.UpdatedAt)
        });
    }
    private static ScalarValue Integer(int? value) => value is { } number ? new NumericValue(new ResourceNumber(number)) : new NullValue();
    private static ScalarValue Number(string? value) => value is null ? new NullValue() : new NumericValue(ResourceNumber.Parse(value));
}
