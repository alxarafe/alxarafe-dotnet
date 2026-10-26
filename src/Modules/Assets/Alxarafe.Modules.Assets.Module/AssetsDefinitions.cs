using Alxarafe.Resources;

namespace Alxarafe.Modules.Assets;

public sealed class AssetsDefinitions : IResourceDefinitionProvider
{
    public IReadOnlyCollection<ResourceDefinition> GetResourceDefinitions() =>
    [
        Catalogue(AssetResources.Type, [new(AssetFields.Description, ScalarType.Text, nullable: true)]),
        Catalogue(AssetResources.Manufacturer),
        Catalogue(AssetResources.Model, [new(AssetFields.ManufacturerId, ScalarType.Uuid, required: true, immutable: true, capabilities: new(true))],
            [new("manufacturer", RelationKind.ManyToOne, AssetResources.Manufacturer, AssetFields.ManufacturerId, AssetFields.Id, AssetFields.Name, true)]),
        Catalogue(AssetResources.Location), Catalogue(AssetResources.Status),
        AssetDynamicDefinitions.Definition(), AssetDynamicDefinitions.Value(), AssetAssignmentDefinitions.Resource(),
        Definition(AssetResources.Asset,
            [new(AssetFields.Name, ScalarType.String, required: true, constraints: new TextConstraints(1, AssetLimits.NameLength), capabilities: new(true, true)),
                new(AssetFields.AssetTypeId, ScalarType.Uuid, required: true, immutable: true, capabilities: new(true)),
                new(AssetFields.AssetTag, ScalarType.String, nullable: true, constraints: new TextConstraints(1, AssetLimits.NameLength)),
                new(AssetFields.SerialNumber, ScalarType.String, nullable: true),
                new(AssetFields.ManufacturerId, ScalarType.Uuid, nullable: true, capabilities: new(true)),
                new(AssetFields.AssetModelId, ScalarType.Uuid, nullable: true, capabilities: new(true)),
                new(AssetFields.LocationId, ScalarType.Uuid, nullable: true, capabilities: new(true)),
                new(AssetFields.StatusId, ScalarType.Uuid, readOnly: true, capabilities: new(true)),
                new(AssetFields.Lifecycle, ScalarType.Enum, readOnly: true, enumValues: [AssetLifecycle.Draft, AssetLifecycle.Ready, AssetLifecycle.Retired], capabilities: new(true)),
                new(AssetFields.PurchaseDate, ScalarType.Date, nullable: true),
                new(AssetFields.PurchasePrice, ScalarType.Decimal, nullable: true, constraints: new DecimalConstraints(AssetLimits.PricePrecision, AssetLimits.PriceScale, new NumericConstraints(Minimum: ResourceNumber.Zero))),
                new(AssetFields.Condition, ScalarType.UInt8, nullable: true, constraints: new NumericConstraints(ResourceNumber.Zero, ResourceNumber.Parse(AssetLimits.MaximumCondition.ToString(System.Globalization.CultureInfo.InvariantCulture)))),
                new(AssetFields.Notes, ScalarType.Text, nullable: true)], AssetsPermissions.Maintain,
            [new("assetType", RelationKind.ManyToOne, AssetResources.Type, AssetFields.AssetTypeId, AssetFields.Id, AssetFields.Name, true),
                new("manufacturer", RelationKind.ManyToOne, AssetResources.Manufacturer, AssetFields.ManufacturerId, AssetFields.Id, AssetFields.Name),
                new("assetModel", RelationKind.ManyToOne, AssetResources.Model, AssetFields.AssetModelId, AssetFields.Id, AssetFields.Name),
                new("location", RelationKind.ManyToOne, AssetResources.Location, AssetFields.LocationId, AssetFields.Id, AssetFields.Name),
                new("status", RelationKind.ManyToOne, AssetResources.Status, AssetFields.StatusId, AssetFields.Id, AssetFields.Name, true)],
            new ActionDefinition[] { new(AssetActions.Complete, ActionScope.Entity, AssetsPermissions.Maintain), new(AssetActions.Retire, ActionScope.Entity, AssetsPermissions.Sensitive) }.Concat(AssetAssignmentDefinitions.Actions))
    ];

    private static ResourceDefinition Catalogue(string id, IEnumerable<FieldDefinition>? extra = null, IEnumerable<RelationDefinition>? relations = null) =>
        Definition(id, new FieldDefinition[]
        {
            new(AssetFields.Code, ScalarType.String, required: true, immutable: true, constraints: new TextConstraints(1, AssetLimits.CodeLength), capabilities: new(true, true)),
            new(AssetFields.Name, ScalarType.String, required: true, constraints: new TextConstraints(1, AssetLimits.NameLength), capabilities: new(true, true)),
            new(AssetFields.Active, ScalarType.Boolean, defaultValue: new BooleanValue(true), capabilities: new(true))
        }.Concat(extra ?? []), id is AssetResources.Type or AssetResources.Status ? AssetsPermissions.SchemaManage : AssetsPermissions.Maintain, relations);

    internal static ResourceDefinition Definition(string id, IEnumerable<FieldDefinition> fields, string write,
        IEnumerable<RelationDefinition>? relations = null, IEnumerable<ActionDefinition>? actions = null)
    {
        var operations = Enum.GetValues<ResourceOperation>();
        return new(id, AssetsModule.Version, AssetsModule.Id, new(AssetFields.Id), new FieldDefinition[]
        {
            new(AssetFields.Id, ScalarType.Uuid, readOnly: true, immutable: true, capabilities: new(true, true)),
            new(AssetFields.CreatedAt, ScalarType.DateTime, readOnly: true, capabilities: new(Sortable: true)),
            new(AssetFields.UpdatedAt, ScalarType.DateTime, readOnly: true)
        }.Concat(fields), operations, new(operations.Select(operation => new OperationSecurityRule(operation,
            operation is ResourceOperation.Get or ResourceOperation.List ? [AssetsPermissions.Read] : [AssetsPermissions.Read, write]))), relations, actions);
    }
}
