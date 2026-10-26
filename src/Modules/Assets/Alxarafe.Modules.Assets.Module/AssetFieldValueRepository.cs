using Alxarafe.Resources.Runtime;
using Microsoft.EntityFrameworkCore;

namespace Alxarafe.Modules.Assets;

public sealed class AssetFieldValueRepository(IDbContextFactory<AssetsDbContext> contexts) : IResourceRepository
{
    public string ResourceId => AssetResources.FieldValue;
    public async Task<ResourcePage> ListAsync(ResourceQuery query, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await AssetsPersistence.Page(db.FieldValues.AsNoTracking().Include(row => row.Definition), query, AssetDynamicRecords.Value, cancellationToken);
    }
    public async Task<ResourceRecord?> GetAsync(ResourceIdentifier identifier, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await db.FieldValues.AsNoTracking().Include(row => row.Definition).SingleOrDefaultAsync(row => row.Id == AssetsPersistence.Identifier(identifier), cancellationToken);
        return row is null ? null : AssetDynamicRecords.Value(row);
    }
    public async Task<ResourceRecord> CreateAsync(ResourceRecord values, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await AssetsPersistence.Mutation(db, cancellationToken);
        var asset = await Asset(db, AssetsPersistence.Uuid(values, AssetFields.AssetId), cancellationToken);
        var definitionId = AssetsPersistence.Uuid(values, AssetFields.FieldDefinitionId);
        var definition = await db.FieldDefinitions.SingleOrDefaultAsync(field => field.Id == definitionId, cancellationToken)
            ?? throw new ResourceRuntimeException(AssetResources.FieldDefinition, ResourceRuntimeError.ResourceNotFound);
        Writable(asset, definition);
        var value = AssetDynamicSchema.Input(definition, values);
        if (await db.FieldValues.AnyAsync(row => row.AssetId == asset.Id && row.FieldDefinitionId == definition.Id, cancellationToken))
            throw new ResourceConflictException(AssetFailures.Duplicate);
        var row = new AssetFieldValueRow { Id = Guid.NewGuid(), AssetId = asset.Id, AssetTypeId = asset.AssetTypeId, FieldDefinitionId = definition.Id,
            Definition = definition, ValueJson = AssetDynamicSchema.Encode(value), CreatedAt = AssetsPersistence.Now(), UpdatedAt = AssetsPersistence.Now() };
        asset.UpdatedAt = row.UpdatedAt;
        db.FieldValues.Add(row); await AssetsPersistence.Save(db, cancellationToken);
        if (asset.Lifecycle == AssetLifecycle.Ready) await AssetDynamicFields.RequireComplete(db, asset, cancellationToken);
        await transaction.CommitAsync(cancellationToken); return AssetDynamicRecords.Value(row);
    }
    public async Task<ResourceRecord?> UpdateAsync(ResourceIdentifier identifier, ResourceRecord patch, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await AssetsPersistence.Mutation(db, cancellationToken);
        var row = await db.FieldValues.Include(row => row.Definition).SingleOrDefaultAsync(row => row.Id == AssetsPersistence.Identifier(identifier), cancellationToken);
        if (row is null) return null;
        var asset = await Asset(db, row.AssetId, cancellationToken);
        Writable(asset, row.Definition);
        if (patch.Values.ContainsKey(AssetFields.AssetId) || patch.Values.ContainsKey(AssetFields.FieldDefinitionId))
            throw new ResourceConflictException(ResourceConflictException.ImmutableField);
        row.ValueJson = AssetDynamicSchema.Encode(AssetDynamicSchema.Input(row.Definition, patch));
        asset.UpdatedAt = row.UpdatedAt = AssetsPersistence.Now();
        await AssetsPersistence.Save(db, cancellationToken);
        if (asset.Lifecycle == AssetLifecycle.Ready) await AssetDynamicFields.RequireComplete(db, asset, cancellationToken);
        await transaction.CommitAsync(cancellationToken); return AssetDynamicRecords.Value(row);
    }
    public async Task<bool> DeleteAsync(ResourceIdentifier identifier, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await AssetsPersistence.Mutation(db, cancellationToken);
        var row = await db.FieldValues.Include(row => row.Definition).SingleOrDefaultAsync(row => row.Id == AssetsPersistence.Identifier(identifier), cancellationToken);
        if (row is null) return false;
        var asset = await Asset(db, row.AssetId, cancellationToken);
        Writable(asset, row.Definition);
        if (row.Definition.Required && asset.Lifecycle == AssetLifecycle.Ready) throw new ResourceConflictException(AssetFailures.RequiredValue);
        db.FieldValues.Remove(row); asset.UpdatedAt = AssetsPersistence.Now();
        await AssetsPersistence.Save(db, cancellationToken);
        if (asset.Lifecycle == AssetLifecycle.Ready) await AssetDynamicFields.RequireComplete(db, asset, cancellationToken);
        await transaction.CommitAsync(cancellationToken); return true;
    }
    private static async Task<AssetRow> Asset(AssetsDbContext db, Guid id, CancellationToken ct) =>
        await db.Assets.SingleOrDefaultAsync(row => row.Id == id, ct) ?? throw new ResourceRuntimeException(AssetResources.Asset, ResourceRuntimeError.ResourceNotFound);
    private static void Writable(AssetRow asset, AssetFieldDefinitionRow definition)
    {
        AssetRules.Writable(asset);
        if (asset.AssetTypeId != definition.AssetTypeId) throw new ResourceConflictException(AssetFailures.FieldTypeMismatch);
        if (!definition.Active) throw new ResourceConflictException(AssetFailures.InactiveField);
    }
}
