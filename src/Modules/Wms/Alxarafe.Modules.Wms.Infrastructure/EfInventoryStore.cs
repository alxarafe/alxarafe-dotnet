using System.Data;
using System.Numerics;
using Alxarafe.Modules.Wms.Application;
using Alxarafe.Modules.Wms.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Alxarafe.Modules.Wms.Infrastructure;

public sealed class EfInventoryStore(IDbContextFactory<WmsDbContext> factory) : IInventoryStore
{
    private const string InsertMovementSql = "INSERT INTO " + Migrations.InventoryFoundation.MovementTable + " ("
        + Migrations.InventoryFoundation.MovementColumns + ") VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8})"
        + " ON CONFLICT (warehouse_id, external_key) DO NOTHING";
    public Task<InventoryResult<Warehouse>> CreateWarehouseAsync(Warehouse warehouse, CancellationToken ct) => InsertAsync(warehouse, ct);
    public Task<InventoryResult<Location>> CreateLocationAsync(Location location, CancellationToken ct) => InsertAsync(location, ct);
    public Task<InventoryResult<StockItem>> CreateItemAsync(StockItem item, CancellationToken ct) => InsertAsync(item, ct);

    private async Task<InventoryResult<T>> InsertAsync<T>(T value, CancellationToken ct) where T : class
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        db.Add(value);
        try
        {
            await db.SaveChangesAsync(ct);
            return new(InventoryOutcome.Created, value);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { return new(InventoryOutcome.Conflict); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        { return new(InventoryOutcome.ReferenceNotFound); }
    }

    public async Task<InventoryResult<InventoryMovement>> RecordAsync(InventoryMovement movement, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        // The unique index serializes simultaneous retries. Read the winner using a fresh
        // READ COMMITTED statement after ON CONFLICT, including its complete payload.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var existing = await db.Set<InventoryMovement>().AsNoTracking().SingleOrDefaultAsync(
            row => row.WarehouseId == movement.WarehouseId && row.ExternalKey == movement.ExternalKey, ct);
        if (existing is not null) return Replay(existing, movement);
        if (!await db.Set<Location>().AnyAsync(row => row.Id == movement.LocationId && row.WarehouseId == movement.WarehouseId, ct)
            || !await db.Set<StockItem>().AnyAsync(row => row.Id == movement.ItemId && row.BaseUomId == movement.UomId, ct))
            return new(InventoryOutcome.ReferenceNotFound);
        if (!await db.Set<Location>().AnyAsync(row => row.Id == movement.LocationId && row.Enabled, ct))
            return new(InventoryOutcome.LocationDisabled);
        int inserted = await db.Database.ExecuteSqlRawAsync(InsertMovementSql,
            [movement.Id, movement.WarehouseId, movement.ItemId, movement.LocationId, movement.OccurredAt,
                movement.QuantityDelta, movement.UomId, movement.Type, movement.ExternalKey], ct);
        if (inserted == 0)
        {
            existing = await db.Set<InventoryMovement>().AsNoTracking().SingleAsync(
                row => row.WarehouseId == movement.WarehouseId && row.ExternalKey == movement.ExternalKey, ct);
            return Replay(existing, movement);
        }
        await transaction.CommitAsync(ct);
        return new(InventoryOutcome.Created, movement);
    }

    private static InventoryResult<InventoryMovement> Replay(InventoryMovement existing, InventoryMovement requested) =>
        existing.SameFactAs(requested)
            ? new(InventoryOutcome.Replayed, existing) : new(InventoryOutcome.Conflict);

    public async Task<InventoryWindow<Warehouse>> WarehousesAsync(InventoryFilter filter, BigInteger offset, int limit, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.Set<Warehouse>().AsNoTracking().Where(row => (filter.Id == null || row.Id == filter.Id) && (filter.Code == null || row.Code == filter.Code));
        return await PageAsync(db, query.OrderBy(row => row.Code), offset, limit, ct);
    }
    public async Task<InventoryWindow<Location>> LocationsAsync(InventoryFilter filter, BigInteger offset, int limit, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.Set<Location>().AsNoTracking().Where(row => (filter.WarehouseId == null || row.WarehouseId == filter.WarehouseId)
            && (filter.Id == null || row.Id == filter.Id) && (filter.Code == null || row.Code == filter.Code));
        return await PageAsync(db, query.OrderBy(row => row.WarehouseId).ThenBy(row => row.Code), offset, limit, ct);
    }
    public async Task<InventoryWindow<StockItem>> ItemsAsync(InventoryFilter filter, BigInteger offset, int limit, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await PageAsync(db, db.Set<StockItem>().AsNoTracking().Where(row => (filter.Id == null || row.Id == filter.Id) && (filter.Sku == null || row.Sku == filter.Sku))
            .OrderBy(row => row.Sku), offset, limit, ct);
    }
    private static IQueryable<InventoryMovement> Filter(WmsDbContext db, InventoryFilter filter) =>
        db.Set<InventoryMovement>().AsNoTracking().Where(row => (filter.WarehouseId == null || row.WarehouseId == filter.WarehouseId)
            && (filter.Id == null || row.Id == filter.Id) && (filter.ItemId == null || row.ItemId == filter.ItemId) && (filter.LocationId == null || row.LocationId == filter.LocationId)
            && (filter.Sku == null || db.Set<StockItem>().Any(item => item.Id == row.ItemId && item.Sku == filter.Sku)));
    public async Task<InventoryWindow<InventoryMovement>> MovementsAsync(InventoryFilter filter, BigInteger offset, int limit, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = Filter(db, filter).Where(row => (filter.From == null || row.OccurredAt >= filter.From)
            && (filter.To == null || row.OccurredAt < filter.To));
        return await PageAsync(db, query.OrderBy(row => row.OccurredAt).ThenBy(row => row.Id), offset, limit, ct);
    }
    public async Task<InventoryWindow<InventoryBalance>> BalancesAsync(InventoryFilter filter, BigInteger offset, int limit, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = Filter(db, filter).GroupBy(row => new { row.WarehouseId, row.ItemId, row.LocationId, row.UomId })
            .Select(group => new { group.Key.WarehouseId, group.Key.ItemId, group.Key.LocationId, group.Key.UomId, Quantity = group.Sum(row => row.QuantityDelta) })
            .Where(row => row.Quantity != 0).OrderBy(row => row.WarehouseId).ThenBy(row => row.ItemId).ThenBy(row => row.LocationId)
            .Select(row => new InventoryBalance(row.WarehouseId, row.ItemId, row.LocationId, row.UomId, row.Quantity));
        return await PageAsync(db, query, offset, limit, ct);
    }
    private static async Task<InventoryWindow<T>> PageAsync<T>(WmsDbContext db, IQueryable<T> query, BigInteger offset, int limit, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        long total = await query.LongCountAsync(ct);
        // PostgreSQL OFFSET accepts bigint; EF Skip accepts int. Raw OFFSET is only
        // needed above int.MaxValue, where these small catalogues still return empty.
        if (offset > int.MaxValue && offset < total) throw new InvalidOperationException("Inventory page offset exceeds supported query capacity.");
        var rows = offset >= total ? [] : await query.Skip((int)offset).Take(limit).ToArrayAsync(ct);
        await transaction.CommitAsync(ct);
        return new(rows, total);
    }
}
