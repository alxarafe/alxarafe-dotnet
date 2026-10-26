using Alxarafe.Modularity;
using Alxarafe.Resources.Runtime;
using Microsoft.EntityFrameworkCore;

namespace Alxarafe.Modules.Assets;

public sealed class AssetFieldDefinitionRepository(IDbContextFactory<AssetsDbContext> contexts, ModuleRegistry modules) : IResourceRepository
{
    public string ResourceId => AssetResources.FieldDefinition;
    public async Task<ResourcePage> ListAsync(ResourceQuery query, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await AssetsPersistence.Page(db.FieldDefinitions.AsNoTracking(), query, AssetDynamicRecords.Definition, cancellationToken);
    }
    public async Task<ResourceRecord?> GetAsync(ResourceIdentifier identifier, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await db.FieldDefinitions.AsNoTracking().SingleOrDefaultAsync(row => row.Id == AssetsPersistence.Identifier(identifier), cancellationToken);
        return row is null ? null : AssetDynamicRecords.Definition(row);
    }
    public async Task<ResourceRecord> CreateAsync(ResourceRecord values, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await AssetsPersistence.Mutation(db, cancellationToken);
        var row = new AssetFieldDefinitionRow
        {
            Id = Guid.NewGuid(), AssetTypeId = AssetsPersistence.Uuid(values, AssetFields.AssetTypeId),
            Code = AssetsPersistence.Text(values, AssetFields.Code), FieldType = AssetDynamicSchema.Type(AssetsPersistence.Text(values, AssetFields.FieldType)),
            CreatedAt = AssetsPersistence.Now(), UpdatedAt = AssetsPersistence.Now()
        };
        AssetDynamicSchema.Patch(row, values);
        AssetDynamicSchema.Validate(row, modules, create: true);
        await AssetsPersistence.Reference<AssetTypeRow>(db, row.AssetTypeId, AssetResources.Type, requireActive: false, cancellationToken);
        await ValidateEvolution(db, row, cancellationToken);
        db.FieldDefinitions.Add(row); await AssetsPersistence.Save(db, cancellationToken); await transaction.CommitAsync(cancellationToken);
        return AssetDynamicRecords.Definition(row);
    }
    public async Task<ResourceRecord?> UpdateAsync(ResourceIdentifier identifier, ResourceRecord patch, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await AssetsPersistence.Mutation(db, cancellationToken);
        var row = await db.FieldDefinitions.SingleOrDefaultAsync(row => row.Id == AssetsPersistence.Identifier(identifier), cancellationToken);
        if (row is null) return null;
        if (patch.Values.ContainsKey(AssetFields.AssetTypeId) || patch.Values.ContainsKey(AssetFields.Code) || patch.Values.ContainsKey(AssetFields.FieldType))
            throw new ResourceConflictException(ResourceConflictException.ImmutableField);
        AssetDynamicSchema.Patch(row, patch);
        AssetDynamicSchema.Validate(row, modules, create: false);
        await ValidateEvolution(db, row, cancellationToken);
        row.UpdatedAt = AssetsPersistence.Now(); await AssetsPersistence.Save(db, cancellationToken); await transaction.CommitAsync(cancellationToken);
        return AssetDynamicRecords.Definition(row);
    }
    public async Task<bool> DeleteAsync(ResourceIdentifier identifier, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await AssetsPersistence.Mutation(db, cancellationToken);
        var row = await db.FieldDefinitions.SingleOrDefaultAsync(row => row.Id == AssetsPersistence.Identifier(identifier), cancellationToken);
        if (row is null) return false;
        if (row.Required || await db.FieldValues.AnyAsync(value => value.FieldDefinitionId == row.Id, cancellationToken))
            throw new ResourceConflictException(AssetFailures.Referenced);
        db.FieldDefinitions.Remove(row); await AssetsPersistence.Save(db, cancellationToken); await transaction.CommitAsync(cancellationToken);
        return true;
    }
    private static async Task ValidateEvolution(AssetsDbContext db, AssetFieldDefinitionRow row, CancellationToken ct)
    {
        if (await db.FieldDefinitions.AnyAsync(other => other.Id != row.Id && other.AssetTypeId == row.AssetTypeId && other.Code == row.Code, ct))
            throw new ResourceConflictException(AssetFailures.Duplicate);
        await AssetDynamicFields.Evolution(db, row, ct);
    }
}
