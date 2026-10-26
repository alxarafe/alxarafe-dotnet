using Alxarafe.Resources;

namespace Alxarafe.Modules.Assets;

internal static class AssetAssignmentDefinitions
{
    internal static ResourceDefinition Resource() => new(AssetResources.Assignment, AssetsModule.Version, AssetsModule.Id, new(AssetFields.Id),
        [new(AssetFields.Id, ScalarType.Uuid, readOnly: true, immutable: true, capabilities: new(true, true)),
            new(AssetFields.TenantId, ScalarType.String, readOnly: true),
            new(AssetFields.AssetId, ScalarType.Uuid, readOnly: true, immutable: true, capabilities: new(true)),
            new(AssetFields.TargetType, ScalarType.Enum, readOnly: true, enumValues: [AssetAssignmentTargets.Location], capabilities: new(true)),
            new(AssetFields.TargetId, ScalarType.Uuid, readOnly: true, immutable: true, capabilities: new(true)),
            new(AssetFields.TargetResourceId, ScalarType.String, readOnly: true),
            new(AssetFields.StartedAt, ScalarType.DateTime, readOnly: true, capabilities: new(true, true)),
            new(AssetFields.EndedAt, ScalarType.DateTime, nullable: true, readOnly: true, capabilities: new(true, true)),
            new(AssetFields.StartReason, ScalarType.String, nullable: true, readOnly: true),
            new(AssetFields.EndReason, ScalarType.String, nullable: true, readOnly: true),
            new(AssetFields.Notes, ScalarType.Text, nullable: true, readOnly: true),
            new(AssetFields.CreatedAt, ScalarType.DateTime, readOnly: true, capabilities: new(Sortable: true)),
            new(AssetFields.UpdatedAt, ScalarType.DateTime, readOnly: true)],
        [ResourceOperation.List, ResourceOperation.Get],
        new(new[] { ResourceOperation.List, ResourceOperation.Get }.Select(operation => new OperationSecurityRule(operation, [AssetsPermissions.Read]))),
        [new("asset", RelationKind.ManyToOne, AssetResources.Asset, AssetFields.AssetId, AssetFields.Id, AssetFields.Name, true),
            new("target", RelationKind.ManyToOne, AssetResources.Location, AssetFields.TargetId, AssetFields.Id, AssetFields.Name, true)]);

    internal static IReadOnlyList<ActionDefinition> Actions =>
    [
        new(AssetActions.Assign, ActionScope.Entity, AssetsPermissions.Assignments, parameters: TargetParameters(requiredReason: false)),
        new(AssetActions.Unassign, ActionScope.Entity, AssetsPermissions.Assignments, parameters: [Reason(required: true)]),
        new(AssetActions.Transfer, ActionScope.Entity, AssetsPermissions.Assignments, parameters: TargetParameters(requiredReason: true))
    ];
    private static IEnumerable<ActionParameterDefinition> TargetParameters(bool requiredReason) =>
    [new(AssetFields.TargetType, ScalarType.Enum, required: true, enumValues: [AssetAssignmentTargets.Location]),
        new(AssetFields.TargetId, ScalarType.Uuid, required: true), Reason(requiredReason),
        new(AssetFields.Notes, ScalarType.Text, constraints: new TextConstraints(MaximumLength: AssetAssignmentLimits.NotesLength, Pattern: AssetAssignmentLimits.NotesPattern))];
    private static ActionParameterDefinition Reason(bool required) => new(AssetFields.Reason, ScalarType.String, required: required,
        constraints: new TextConstraints(1, AssetAssignmentLimits.ReasonLength, AssetAssignmentLimits.ReasonPattern));
}
