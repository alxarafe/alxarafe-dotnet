using Alxarafe.Resources;
using Alxarafe.Resources.Runtime;
using Microsoft.EntityFrameworkCore;

namespace Alxarafe.Modules.Assets;

// Five module-owned catalogues share the same stored fields and CRUD protocol.
// This is an Assets adapter, not a generic persistence facility in Runtime/Core.
public sealed class AssetCatalogueRepository<T>(IDbContextFactory<AssetsDbContext> contexts) : IResourceRepository where T : AssetCatalogueRow, new()
{
    public string ResourceId => typeof(T) == typeof(AssetTypeRow) ? AssetResources.Type
        : typeof(T) == typeof(ManufacturerRow) ? AssetResources.Manufacturer
        : typeof(T) == typeof(AssetModelRow) ? AssetResources.Model
        : typeof(T) == typeof(AssetLocationRow) ? AssetResources.Location : AssetResources.Status;
    public async Task<ResourcePage> ListAsync(ResourceQuery query, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await AssetsPersistence.Page(db.Set<T>().AsNoTracking(), query, row => AssetsRecords.Catalogue(row), cancellationToken);
    }
    public async Task<ResourceRecord?> GetAsync(ResourceIdentifier identifier, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await db.Set<T>().AsNoTracking().SingleOrDefaultAsync(row => row.Id == AssetsPersistence.Identifier(identifier), cancellationToken);
        return row is null ? null : AssetsRecords.Catalogue(row);
    }
    public async Task<ResourceRecord> CreateAsync(ResourceRecord values, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await AssetsPersistence.Mutation(db, cancellationToken);
        var row = new T { Id = Guid.NewGuid(), Code = AssetsPersistence.Text(values, AssetFields.Code), CreatedAt = AssetsPersistence.Now(), UpdatedAt = AssetsPersistence.Now() };
        if (row is AssetModelRow model) model.ManufacturerId = AssetsPersistence.Uuid(values, AssetFields.ManufacturerId);
        Apply(row, values);
        await Validate(db, row, true, cancellationToken);
        db.Add(row); await AssetsPersistence.Save(db, cancellationToken); await transaction.CommitAsync(cancellationToken);
        return AssetsRecords.Catalogue(row);
    }
    public async Task<ResourceRecord?> UpdateAsync(ResourceIdentifier identifier, ResourceRecord patch, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await AssetsPersistence.Mutation(db, cancellationToken);
        var row = await db.Set<T>().SingleOrDefaultAsync(row => row.Id == AssetsPersistence.Identifier(identifier), cancellationToken);
        if (row is null) return null;
        if (patch.Values.ContainsKey(AssetFields.Code) || patch.Values.ContainsKey(AssetFields.ManufacturerId)) throw new ResourceConflictException(ResourceConflictException.ImmutableField);
        Apply(row, patch);
        await Validate(db, row, false, cancellationToken);
        row.UpdatedAt = AssetsPersistence.Now(); await AssetsPersistence.Save(db, cancellationToken); await transaction.CommitAsync(cancellationToken);
        return AssetsRecords.Catalogue(row);
    }
    public async Task<bool> DeleteAsync(ResourceIdentifier identifier, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await AssetsPersistence.Mutation(db, cancellationToken);
        var row = await db.Set<T>().SingleOrDefaultAsync(row => row.Id == AssetsPersistence.Identifier(identifier), cancellationToken);
        if (row is null) return false;
        if (row is AssetStatusRow && AssetStatusCodes.Reserved.Contains(row.Code, StringComparer.Ordinal)) throw new ResourceConflictException(AssetFailures.ReservedStatus);
        bool referenced = row switch
        {
            AssetTypeRow => await db.Assets.AnyAsync(asset => asset.AssetTypeId == row.Id, cancellationToken)
                || await db.FieldDefinitions.AnyAsync(field => field.AssetTypeId == row.Id, cancellationToken),
            ManufacturerRow => await db.Models.AnyAsync(model => model.ManufacturerId == row.Id, cancellationToken) || await db.Assets.AnyAsync(asset => asset.ManufacturerId == row.Id, cancellationToken),
            AssetModelRow => await db.Assets.AnyAsync(asset => asset.AssetModelId == row.Id, cancellationToken),
            AssetLocationRow => await db.Assets.AnyAsync(asset => asset.LocationId == row.Id, cancellationToken)
                || await db.Assignments.AnyAsync(assignment => assignment.TargetId == row.Id, cancellationToken),
            _ => await db.Assets.AnyAsync(asset => asset.StatusId == row.Id, cancellationToken)
        };
        if (referenced) throw new ResourceConflictException(AssetFailures.Referenced);
        db.Remove(row); await AssetsPersistence.Save(db, cancellationToken); await transaction.CommitAsync(cancellationToken);
        return true;
    }
    private async Task Validate(AssetsDbContext db, T row, bool create, CancellationToken ct)
    {
        AssetsPersistence.Nonblank(ResourceId, AssetFields.Code, row.Code);
        AssetsPersistence.Nonblank(ResourceId, AssetFields.Name, row.Name);
        if (row is AssetStatusRow && AssetStatusCodes.Reserved.Contains(row.Code, StringComparer.Ordinal) && !row.Active)
            throw new ResourceConflictException(AssetFailures.ReservedStatus);
        if (row is AssetModelRow model)
        {
            await AssetsPersistence.Reference<ManufacturerRow>(db, model.ManufacturerId, AssetResources.Manufacturer, create, ct);
            if (await db.Models.AnyAsync(other => other.Id != row.Id && other.Code == row.Code && other.ManufacturerId == model.ManufacturerId, ct)) throw new ResourceConflictException(AssetFailures.Duplicate);
        }
        else if (await db.Set<T>().AnyAsync(other => other.Id != row.Id && other.Code == row.Code, ct)) throw new ResourceConflictException(AssetFailures.Duplicate);
    }
    private static void Apply(T row, ResourceRecord values)
    {
        if (values.Values.TryGetValue(AssetFields.Name, out var name)) row.Name = ((TextValue)name).Value;
        if (values.Values.TryGetValue(AssetFields.Active, out var active)) row.Active = ((BooleanValue)active).Value;
        if (row is AssetTypeRow type && values.Values.TryGetValue(AssetFields.Description, out var description)) type.Description = AssetsRecords.OptionalText(description);
    }
}
