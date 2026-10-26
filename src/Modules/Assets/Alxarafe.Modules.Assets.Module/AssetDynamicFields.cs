using Alxarafe.Resources.Runtime;
using Microsoft.EntityFrameworkCore;

namespace Alxarafe.Modules.Assets;

internal static class AssetDynamicFields
{
    internal static async Task RequireComplete(AssetsDbContext db, AssetRow asset, CancellationToken ct)
    {
        var definitions = await db.FieldDefinitions.AsNoTracking().Where(field => field.AssetTypeId == asset.AssetTypeId && field.Active && field.Required).ToArrayAsync(ct);
        var values = await db.FieldValues.AsNoTracking().Where(value => value.AssetId == asset.Id).ToDictionaryAsync(value => value.FieldDefinitionId, ct);
        if (definitions.Any(field => !values.TryGetValue(field.Id, out var value) || !AssetDynamicSchema.Accepts(field, value.ValueJson)))
            throw new ResourceConflictException(AssetFailures.RequiredFields);
    }
    internal static async Task Evolution(AssetsDbContext db, AssetFieldDefinitionRow candidate, CancellationToken ct)
    {
        var values = await db.FieldValues.AsNoTracking().Where(value => value.FieldDefinitionId == candidate.Id).ToArrayAsync(ct);
        if (values.Any(value => !AssetDynamicSchema.Accepts(candidate, value.ValueJson)))
            throw new ResourceConflictException(AssetFailures.IncompatibleDefinition);
        if (candidate.Active && candidate.Required && await db.Assets.AnyAsync(asset => asset.AssetTypeId == candidate.AssetTypeId
            && asset.Lifecycle != AssetLifecycle.Draft && !db.FieldValues.Any(value => value.AssetId == asset.Id && value.FieldDefinitionId == candidate.Id), ct))
            throw new ResourceConflictException(AssetFailures.IncompatibleDefinition);
    }
}
