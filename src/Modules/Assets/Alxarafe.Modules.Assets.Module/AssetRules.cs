using Alxarafe.Resources;
using Alxarafe.Resources.Runtime;

namespace Alxarafe.Modules.Assets;

internal static class AssetRules
{
    internal static void Writable(AssetRow row)
    {
        if (row.Lifecycle == AssetLifecycle.Retired) throw new ResourceConflictException(AssetFailures.Retired);
        if (row.Lifecycle is not (AssetLifecycle.Draft or AssetLifecycle.Ready)) throw new ResourceConflictException(AssetFailures.InvalidTransition);
    }
    internal static void Complete(AssetRow row, ResourceDefinition definition)
    {
        if (row.Lifecycle != AssetLifecycle.Draft) throw new ResourceConflictException(AssetFailures.InvalidTransition);
        if (string.IsNullOrWhiteSpace(row.Name)) throw new ResourceConflictException(AssetFailures.InvalidTransition);
        var values = AssetsRecords.Asset(row).Values;
        if (definition.Fields.Any(field => values.TryGetValue(field.Name, out var value)
            ? !ResourceValueValidator.IsValid(field, value) : field.Required))
            throw new ResourceConflictException(AssetFailures.InvalidTransition);
        row.Lifecycle = AssetLifecycle.Ready;
    }
    internal static void Retire(AssetRow row, AssetStatusRow status)
    {
        if (row.Lifecycle != AssetLifecycle.Ready || status.Code == AssetStatusCodes.Assigned)
            throw new ResourceConflictException(AssetFailures.InvalidTransition);
        row.Lifecycle = AssetLifecycle.Retired;
    }
}
