using Alxarafe.Resources;
using Alxarafe.Resources.AspNetCore;

namespace Alxarafe.Modules.Assets;

internal static class AssetDynamicDefinitions
{
    internal static ResourceDefinition Definition() => AssetsDefinitions.Definition(AssetResources.FieldDefinition,
        [new(AssetFields.AssetTypeId, ScalarType.Uuid, required: true, immutable: true, capabilities: new(true)),
            new(AssetFields.Code, ScalarType.String, required: true, immutable: true, constraints: new TextConstraints(1, AssetLimits.CodeLength), capabilities: new(true, true)),
            new(AssetFields.Name, ScalarType.String, required: true, constraints: new TextConstraints(1, AssetLimits.NameLength), capabilities: new(true, true)),
            new(AssetFields.Description, ScalarType.Text, nullable: true),
            new(AssetFields.FieldType, ScalarType.Enum, required: true, immutable: true, enumValues: AssetDynamicSchema.Types.Select(AssetDynamicSchema.TypeName)),
            new(AssetFields.Required, ScalarType.Boolean, defaultValue: new BooleanValue(false)),
            new(AssetFields.Active, ScalarType.Boolean, defaultValue: new BooleanValue(true), capabilities: new(true)),
            new(AssetFields.DisplayOrder, ScalarType.Int32, defaultValue: new NumericValue(ResourceNumber.Zero), constraints: new NumericConstraints(Minimum: ResourceNumber.Zero), capabilities: new(Sortable: true)),
            new(AssetFields.MinimumLength, ScalarType.Int32, nullable: true, constraints: new NumericConstraints(Minimum: ResourceNumber.Zero)),
            new(AssetFields.MaximumLength, ScalarType.Int32, nullable: true, constraints: new NumericConstraints(Minimum: ResourceNumber.Zero)),
            new(AssetFields.Minimum, ScalarType.Decimal, nullable: true), new(AssetFields.Maximum, ScalarType.Decimal, nullable: true),
            new(AssetFields.Precision, ScalarType.Int32, nullable: true, constraints: new NumericConstraints(ResourceNumber.One, new ResourceNumber(ResourceTransport.MaximumNumericDigits))),
            new(AssetFields.Scale, ScalarType.Int32, nullable: true, constraints: new NumericConstraints(ResourceNumber.Zero, new ResourceNumber(ResourceTransport.MaximumNumericDigits))),
            new(AssetFields.ValueField, ScalarType.String, readOnly: true)], AssetsPermissions.SchemaManage,
        [new("assetType", RelationKind.ManyToOne, AssetResources.Type, AssetFields.AssetTypeId, AssetFields.Id, AssetFields.Name, true)]);

    internal static ResourceDefinition Value() => AssetsDefinitions.Definition(AssetResources.FieldValue,
        new FieldDefinition[]
        {
            new(AssetFields.AssetId, ScalarType.Uuid, required: true, immutable: true, capabilities: new(true)),
            new(AssetFields.FieldDefinitionId, ScalarType.Uuid, required: true, immutable: true, capabilities: new(true)),
            new(AssetFields.AssetTypeId, ScalarType.Uuid, readOnly: true, capabilities: new(true))
        }.Concat(AssetDynamicSchema.Types.Select(type => new FieldDefinition(AssetDynamicSchema.Slot(type), type, capabilities: new(Filterable: true)))),
        AssetsPermissions.Maintain,
        [new("asset", RelationKind.ManyToOne, AssetResources.Asset, AssetFields.AssetId, AssetFields.Id, AssetFields.Name, true),
            new("fieldDefinition", RelationKind.ManyToOne, AssetResources.FieldDefinition, AssetFields.FieldDefinitionId, AssetFields.Id, AssetFields.Name, true)]);
}
