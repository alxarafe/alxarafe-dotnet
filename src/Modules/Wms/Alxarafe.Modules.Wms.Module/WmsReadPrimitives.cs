using System.Text.Json;
using Alxarafe.AspNetCore;
using Alxarafe.Modularity;
using Alxarafe.Modules.Wms.Domain;
using Alxarafe.Modules.Wms.Http;

namespace Alxarafe.Modules.Wms;

public static class WmsReadPrimitives
{
    public const string Warehouses = "wms.warehouses.list", Locations = "wms.locations.list", Items = "wms.items.list",
        Inventory = "wms.inventory.list", Movements = "wms.movements.list";
    public const string Id = "id", Code = "code", Sku = "sku", WarehouseId = "warehouseId", ItemId = "itemId", LocationId = "locationId",
        From = "from", To = "to";
    private static readonly string[] StockFilters = [WarehouseId, ItemId, LocationId, Sku];
    public static readonly IReadOnlyList<ReadPrimitiveDescriptor> All = Array.AsReadOnly(new[]
    {
        Define(Warehouses, "wms_warehouses_list", "List registered warehouses; resolve an exact stable code or identifier.", InventoryPermissions.WarehousesRead, [Id, Code]),
        Define(Locations, "wms_locations_list", "List registered warehouse locations and their codes. Resolve location identifiers for stock results; filter by warehouse or exact code.", InventoryPermissions.LocationsRead, [Id, WarehouseId, Code]),
        Define(Items, "wms_items_list", "List stockable references with SKU, description and base unit code. Resolve a SKU to its identifier or obtain reference labels for stock results.", InventoryPermissions.ItemsRead, [Id, Sku]),
        Define(Inventory, "wms_inventory_list", "Read currently registered inventory balances, including unit codes, by warehouse, reference and location. These are recorded balances, not observed physical stock. Zero balances are omitted.", InventoryPermissions.InventoryRead, StockFilters),
        Define(Movements, "wms_movements_list", "List registered historical inventory movements ordered by business occurrence then identifier, ascending. For last-activity questions retrieve the complete relevant history, not only movements before a cutoff. Date filters are a half-open interval [from,to).", InventoryPermissions.MovementsRead, [Id, .. StockFilters, From, To])
    });
    private static ReadPrimitiveDescriptor Define(string id, string name, string description, string permission, string[] filters)
    {
        var properties = new Dictionary<string, object>
        {
            [CollectionPagination.OffsetParameter] = new { type = "integer", minimum = 0, description = "Zero-based row offset." },
            [CollectionPagination.LimitParameter] = new
            {
                type = "integer",
                minimum = CollectionPagination.MinimumLimit,
                maximum = CollectionPagination.MaximumLimit,
                @default = CollectionPagination.DefaultLimit
            }
        };
        foreach (string field in filters)
            properties[field] = field is From or To
                ? new { type = "string", format = "date-time", description = "Offset timestamp; microsecond precision." }
                : field is Code or Sku ? new { type = "string", pattern = InventoryRules.CodePattern.Replace("\\A", "^").Replace("\\z", "$"), description = "Exact known code; omit if unknown." }
                : new { type = "string", format = "uuid" };
        return new(id, WmsModule.Id, name, description + " Filters combine with AND; omit unknown or unneeded filters.",
            JsonSerializer.SerializeToElement(new { type = "object", properties, additionalProperties = false }), [permission]);
    }
}
