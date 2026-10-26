using Alxarafe.Resources;
using Alxarafe.Resources.Runtime;
using Microsoft.EntityFrameworkCore;

namespace Alxarafe.Modules.Assets;

public sealed class AssetRepository(IDbContextFactory<AssetsDbContext> contexts) : IResourceRepository
{
    public string ResourceId => AssetResources.Asset;
    public async Task<ResourcePage> ListAsync(ResourceQuery query, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await AssetsPersistence.Page(db.Assets.AsNoTracking(), query, AssetsRecords.Asset, cancellationToken);
    }
    public async Task<ResourceRecord?> GetAsync(ResourceIdentifier identifier, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await db.Assets.AsNoTracking().SingleOrDefaultAsync(row => row.Id == AssetsPersistence.Identifier(identifier), cancellationToken);
        return row is null ? null : AssetsRecords.Asset(row);
    }
    public async Task<ResourceRecord> CreateAsync(ResourceRecord values, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await AssetsPersistence.Mutation(db, cancellationToken);
        var row = new AssetRow { Id = Guid.NewGuid(), AssetTypeId = AssetsPersistence.Uuid(values, AssetFields.AssetTypeId),
            CreatedAt = AssetsPersistence.Now(), UpdatedAt = AssetsPersistence.Now() };
        AssetsRecords.Patch(row, values);
        row.StatusId = (await db.Statuses.SingleAsync(status => status.Code == AssetStatusCodes.InStock, cancellationToken)).Id;
        await ValidateReferences(db, row, null, cancellationToken);
        await UniqueTag(db, row, cancellationToken);
        db.Assets.Add(row); await AssetsPersistence.Save(db, cancellationToken); await transaction.CommitAsync(cancellationToken);
        return AssetsRecords.Asset(row);
    }
    public async Task<ResourceRecord?> UpdateAsync(ResourceIdentifier identifier, ResourceRecord patch, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await AssetsPersistence.Mutation(db, cancellationToken);
        var row = await db.Assets.SingleOrDefaultAsync(row => row.Id == AssetsPersistence.Identifier(identifier), cancellationToken);
        if (row is null) return null;
        AssetRules.Writable(row);
        if (patch.Values.ContainsKey(AssetFields.AssetTypeId)) throw new ResourceConflictException(ResourceConflictException.ImmutableField);
        var previous = AssetsRecords.Asset(row);
        AssetsRecords.Patch(row, patch);
        await ValidateReferences(db, row, previous, cancellationToken);
        await UniqueTag(db, row, cancellationToken);
        if (row.Lifecycle == AssetLifecycle.Ready) await AssetDynamicFields.RequireComplete(db, row, cancellationToken);
        row.UpdatedAt = AssetsPersistence.Now();
        await AssetsPersistence.Save(db, cancellationToken); await transaction.CommitAsync(cancellationToken);
        return AssetsRecords.Asset(row);
    }
    public async Task<bool> DeleteAsync(ResourceIdentifier identifier, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await AssetsPersistence.Mutation(db, cancellationToken);
        var row = await db.Assets.SingleOrDefaultAsync(row => row.Id == AssetsPersistence.Identifier(identifier), cancellationToken);
        if (row is null) return false;
        AssetRules.Writable(row);
        var status = await db.Statuses.SingleAsync(status => status.Id == row.StatusId, cancellationToken);
        if (status.Code == AssetStatusCodes.Assigned) throw new ResourceConflictException(AssetFailures.InvalidTransition);
        if (await db.FieldValues.AnyAsync(value => value.AssetId == row.Id, cancellationToken)) throw new ResourceConflictException(AssetFailures.Referenced);
        db.Assets.Remove(row); await AssetsPersistence.Save(db, cancellationToken); await transaction.CommitAsync(cancellationToken);
        return true;
    }
    internal static async Task ValidateReferences(AssetsDbContext db, AssetRow row, ResourceRecord? previous, CancellationToken ct)
    {
        AssetsPersistence.Nonblank(AssetResources.Asset, AssetFields.Name, row.Name);
        await AssetsPersistence.Reference<AssetTypeRow>(db, row.AssetTypeId, AssetResources.Type, previous is null, ct);
        bool Changed(string field, Guid id) => previous is null || previous.Values[field] != new UuidValue(id);
        if (row.ManufacturerId is { } manufacturer) await AssetsPersistence.Reference<ManufacturerRow>(db, manufacturer, AssetResources.Manufacturer, Changed(AssetFields.ManufacturerId, manufacturer), ct);
        if (row.LocationId is { } location) await AssetsPersistence.Reference<AssetLocationRow>(db, location, AssetResources.Location, Changed(AssetFields.LocationId, location), ct);
        if (row.AssetModelId is { } modelId)
        {
            var model = await AssetsPersistence.Reference<AssetModelRow>(db, modelId, AssetResources.Model, Changed(AssetFields.AssetModelId, modelId), ct);
            if (row.ManufacturerId != model.ManufacturerId) throw new ResourceConflictException(AssetFailures.ModelManufacturer);
        }
    }
    private static async Task UniqueTag(AssetsDbContext db, AssetRow row, CancellationToken ct)
    {
        if (row.AssetTag is not null && await db.Assets.AnyAsync(other => other.Id != row.Id && other.AssetTag == row.AssetTag, ct))
            throw new ResourceConflictException(AssetFailures.Duplicate);
    }
}
