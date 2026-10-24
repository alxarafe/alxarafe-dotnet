using System.Numerics;
using Alxarafe.Modules.Wms.Domain;

namespace Alxarafe.Modules.Wms.Application;

public enum InventoryOutcome { Created, Replayed, InvalidRequest, Conflict, ReferenceNotFound, LocationDisabled }
public sealed record InventoryResult<T>(InventoryOutcome Outcome, T? Value = default);
public sealed record InventoryWindow<T>(IReadOnlyList<T> Items, long Total);
public sealed record InventoryFilter(Guid? WarehouseId = null, Guid? ItemId = null, Guid? LocationId = null,
    string? Code = null, string? Sku = null, DateTimeOffset? From = null, DateTimeOffset? To = null, Guid? Id = null);

public sealed record InventoryReadLabels(IReadOnlyDictionary<Guid, string> Warehouses,
    IReadOnlyDictionary<Guid, string> Locations, IReadOnlyDictionary<Guid, string> Items, IReadOnlyDictionary<Guid, string> Units);

public interface IInventoryStore
{
    Task<InventoryReadLabels> ReadLabelsAsync(IReadOnlyCollection<Guid> warehouseIds, IReadOnlyCollection<Guid> locationIds,
        IReadOnlyCollection<Guid> itemIds, IReadOnlyCollection<Guid> unitIds, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, string>> UnitCodesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
    Task<InventoryResult<Warehouse>> CreateWarehouseAsync(Warehouse warehouse, CancellationToken ct);
    Task<InventoryResult<Location>> CreateLocationAsync(Location location, CancellationToken ct);
    Task<InventoryResult<StockItem>> CreateItemAsync(StockItem item, CancellationToken ct);
    Task<InventoryResult<InventoryMovement>> RecordAsync(InventoryMovement movement, CancellationToken ct);
    Task<InventoryWindow<Warehouse>> WarehousesAsync(InventoryFilter filter, BigInteger offset, int limit, CancellationToken ct);
    Task<InventoryWindow<Location>> LocationsAsync(InventoryFilter filter, BigInteger offset, int limit, CancellationToken ct);
    Task<InventoryWindow<StockItem>> ItemsAsync(InventoryFilter filter, BigInteger offset, int limit, CancellationToken ct);
    Task<InventoryWindow<InventoryMovement>> MovementsAsync(InventoryFilter filter, BigInteger offset, int limit, CancellationToken ct);
    Task<InventoryWindow<InventoryBalance>> BalancesAsync(InventoryFilter filter, BigInteger offset, int limit, CancellationToken ct);
}

public sealed class InventoryService(IInventoryStore store)
{
    public Task<InventoryResult<Warehouse>> CreateWarehouseAsync(string? code, string? name, CancellationToken ct) =>
        Warehouse.Create(Guid.NewGuid(), code, name) is { } value ? store.CreateWarehouseAsync(value, ct) : Invalid<Warehouse>();
    public Task<InventoryResult<Location>> CreateLocationAsync(Guid warehouseId, string? code, bool enabled, CancellationToken ct) =>
        Location.Create(Guid.NewGuid(), warehouseId, code, enabled) is { } value ? store.CreateLocationAsync(value, ct) : Invalid<Location>();
    public Task<InventoryResult<StockItem>> CreateItemAsync(string? sku, string? description, Guid uomId, Guid? familyId, CancellationToken ct) =>
        StockItem.Create(Guid.NewGuid(), sku, description, uomId, familyId) is { } value ? store.CreateItemAsync(value, ct) : Invalid<StockItem>();
    public Task<InventoryResult<InventoryMovement>> RecordAsync(Guid warehouseId, Guid itemId, Guid locationId,
        DateTimeOffset occurredAt, decimal delta, Guid uomId, string? type, string? externalKey, CancellationToken ct) =>
        InventoryMovement.Create(Guid.NewGuid(), warehouseId, itemId, locationId, occurredAt, delta, uomId, type, externalKey) is { } value
            ? store.RecordAsync(value, ct) : Invalid<InventoryMovement>();
    private static Task<InventoryResult<T>> Invalid<T>() => Task.FromResult(new InventoryResult<T>(InventoryOutcome.InvalidRequest));
}
