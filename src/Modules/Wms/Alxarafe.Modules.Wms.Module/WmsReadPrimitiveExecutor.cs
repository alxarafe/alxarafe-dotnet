using System.Numerics;
using System.Text.Json;
using Alxarafe.AspNetCore;
using Alxarafe.Modularity;
using Alxarafe.Modules.Wms.Application;
using Alxarafe.Modules.Wms.Domain;
using Alxarafe.Modules.Wms.Http;

namespace Alxarafe.Modules.Wms;

internal sealed class WmsReadPrimitiveExecutor(IInventoryStore store) : IReadPrimitiveExecutor
{
    public bool CanExecute(string primitiveId) => WmsReadPrimitives.All.Any(primitive => primitive.Id == primitiveId);
    public async Task<object?> ExecuteAsync(ReadPrimitiveDescriptor primitive, JsonElement input, ReadPrimitiveContext context, CancellationToken ct)
    {
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var schema = primitive.InputSchema.GetProperty("properties");
        foreach (var property in input.EnumerateObject())
            if (!schema.TryGetProperty(property.Name, out _) || !values.TryAdd(property.Name, property.Value)) throw new ReadPrimitiveInputException();
        string? Number(string field) => !values.TryGetValue(field, out var value) ? null
            : value.ValueKind == JsonValueKind.Number ? value.GetRawText() : throw new ReadPrimitiveInputException();
        var pagination = CollectionPagination.Parse(Number(CollectionPagination.OffsetParameter), Number(CollectionPagination.LimitParameter))
            ?? throw new ReadPrimitiveInputException();
        string? Text(string field)
        {
            if (!values.TryGetValue(field, out var value)) return null;
            return value.ValueKind == JsonValueKind.String ? value.GetString() : throw new ReadPrimitiveInputException();
        }
        Guid? Identifier(string field)
        {
            string? value = Text(field);
            if (value is null) return null;
            return Guid.TryParseExact(value, "D", out var id) && id != Guid.Empty ? id : throw new ReadPrimitiveInputException();
        }
        string? Code(string field)
        {
            string? value = Text(field);
            return value is null || InventoryRules.ValidCode(value) ? value : throw new ReadPrimitiveInputException();
        }
        DateTimeOffset? Timestamp(string field)
        {
            string? value = Text(field);
            if (value is null) return null;
            return InventoryEndpoints.TryTimestamp(value, out var time) ? time.ToUniversalTime() : throw new ReadPrimitiveInputException();
        }
        var filter = new InventoryFilter(Identifier(WmsReadPrimitives.WarehouseId), Identifier(WmsReadPrimitives.ItemId), Identifier(WmsReadPrimitives.LocationId),
            Code(WmsReadPrimitives.Code), Code(WmsReadPrimitives.Sku), Timestamp(WmsReadPrimitives.From), Timestamp(WmsReadPrimitives.To), Identifier(WmsReadPrimitives.Id));
        if (filter.From.HasValue && filter.To.HasValue && filter.From >= filter.To) throw new ReadPrimitiveInputException();
        BigInteger offset = pagination.Offset;
        int limit = pagination.Limit;
        switch (primitive.Id)
        {
            case WmsReadPrimitives.Warehouses:
                return Page(await store.WarehousesAsync(filter, offset, limit, ct), [WmsReadPrimitives.Code]);
            case WmsReadPrimitives.Locations:
                return Page(await store.LocationsAsync(filter, offset, limit, ct), [WmsReadPrimitives.WarehouseId, WmsReadPrimitives.Code]);
            case WmsReadPrimitives.Items:
                var items = await store.ItemsAsync(filter, offset, limit, ct);
                var baseUnits = await store.UnitCodesAsync(items.Items.Select(item => item.BaseUomId).Distinct().ToArray(), ct);
                return Page(new InventoryWindow<object>(items.Items.Select(item => (object)new { item.Id, item.Sku, item.Description, baseUom = baseUnits[item.BaseUomId] }).ToArray(), items.Total), [WmsReadPrimitives.Sku]);
            case WmsReadPrimitives.Inventory:
                var balances = await store.BalancesAsync(filter, offset, limit, ct);
                var labels = await store.ReadLabelsAsync(balances.Items.Select(row => row.WarehouseId).Distinct().ToArray(),
                    balances.Items.Select(row => row.LocationId).Distinct().ToArray(), balances.Items.Select(row => row.ItemId).Distinct().ToArray(),
                    balances.Items.Select(row => row.UomId).Distinct().ToArray(), ct);
                return Page(new InventoryWindow<object>(balances.Items.Select(row => (object)new
                {
                    warehouseCode = labels.Warehouses[row.WarehouseId],
                    sku = labels.Items[row.ItemId],
                    locationCode = labels.Locations[row.LocationId],
                    row.Quantity,
                    uom = labels.Units[row.UomId]
                }).ToArray(), balances.Total),
                    [WmsReadPrimitives.WarehouseId, WmsReadPrimitives.ItemId, WmsReadPrimitives.LocationId]);
            case WmsReadPrimitives.Movements:
                var movements = await store.MovementsAsync(filter, offset, limit, ct);
                var movementLabels = await store.ReadLabelsAsync(movements.Items.Select(row => row.WarehouseId).Distinct().ToArray(),
                    movements.Items.Select(row => row.LocationId).Distinct().ToArray(), movements.Items.Select(row => row.ItemId).Distinct().ToArray(),
                    movements.Items.Select(row => row.UomId).Distinct().ToArray(), ct);
                return Page(new InventoryWindow<object>(movements.Items.Select(row => (object)new
                {
                    warehouseCode = movementLabels.Warehouses[row.WarehouseId],
                    sku = movementLabels.Items[row.ItemId],
                    locationCode = movementLabels.Locations[row.LocationId],
                    row.OccurredAt,
                    row.QuantityDelta,
                    uom = movementLabels.Units[row.UomId],
                    row.Type,
                    reference = row.ExternalKey
                }).ToArray(), movements.Total),
                    ["occurredAt", WmsReadPrimitives.Id]);
            default: throw new ReadPrimitiveInputException();
        }
        InventoryPage<T> Page<T>(InventoryWindow<T> window, string[] fields) =>
            new(window.Items, offset, limit, window.Total, fields.Select(field => new CollectionOrder(field, "asc")).ToArray());
    }
}
