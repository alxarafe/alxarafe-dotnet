using Alxarafe.Core;
using Alxarafe.Resources;
using Alxarafe.Resources.Runtime;
using Microsoft.EntityFrameworkCore;

namespace Alxarafe.Modules.Assets;

public sealed class AssetAssignmentRepository(IDbContextFactory<AssetsDbContext> contexts, IRequestTenantContext tenant) : IResourceRepository
{
    public string ResourceId => AssetResources.Assignment;
    public async Task<ResourcePage> ListAsync(ResourceQuery query, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var owner = await tenant.RequireAsync(cancellationToken);
        return await AssetsPersistence.Page(db.Assignments.AsNoTracking(), query, row => Record(row, owner.Id), cancellationToken);
    }
    public async Task<ResourceRecord?> GetAsync(ResourceIdentifier identifier, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await db.Assignments.AsNoTracking().SingleOrDefaultAsync(row => row.Id == AssetsPersistence.Identifier(identifier), cancellationToken);
        return row is null ? null : Record(row, (await tenant.RequireAsync(cancellationToken)).Id);
    }
    public Task<ResourceRecord> CreateAsync(ResourceRecord values, CancellationToken cancellationToken = default) => throw Unsupported();
    public Task<ResourceRecord?> UpdateAsync(ResourceIdentifier identifier, ResourceRecord patch, CancellationToken cancellationToken = default) => throw Unsupported();
    public Task<bool> DeleteAsync(ResourceIdentifier identifier, CancellationToken cancellationToken = default) => throw Unsupported();
    private ResourceRuntimeException Unsupported() => new(ResourceId, ResourceRuntimeError.OperationNotSupported);
    private static ResourceRecord Record(AssetAssignmentRow row, string tenantId) => new(new Dictionary<string, ScalarValue>(StringComparer.Ordinal)
    {
        [AssetFields.Id] = new UuidValue(row.Id), [AssetFields.TenantId] = new TextValue(tenantId), [AssetFields.AssetId] = new UuidValue(row.AssetId),
        [AssetFields.TargetType] = new TextValue(row.TargetType), [AssetFields.TargetId] = new UuidValue(row.TargetId),
        [AssetFields.TargetResourceId] = new TextValue(AssetResources.Location),
        [AssetFields.StartedAt] = new DateTimeValue(row.StartedAt),
        [AssetFields.EndedAt] = row.EndedAt is { } end ? new DateTimeValue(end) : new NullValue(),
        [AssetFields.StartReason] = Text(row.StartReason), [AssetFields.EndReason] = Text(row.EndReason), [AssetFields.Notes] = Text(row.Notes),
        [AssetFields.CreatedAt] = new DateTimeValue(row.CreatedAt), [AssetFields.UpdatedAt] = new DateTimeValue(row.UpdatedAt)
    });
    private static ScalarValue Text(string? value) => value is null ? new NullValue() : new TextValue(value);
}
