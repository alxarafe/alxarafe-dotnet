using System.Text.RegularExpressions;

namespace Alxarafe.Modules.Wms.Domain;

public static class InventoryRules
{
    public const int QuantityPrecision = 20;
    public const int QuantityScale = 6;
    public const decimal QuantityMaximum = 99999999999999.999999m;
    public const string Receipt = "RECEIPT";
    public const string Issue = "ISSUE";
    public const string Adjustment = "ADJUSTMENT";
    public const string CodePattern = "\\A[A-Z0-9][A-Z0-9_.-]{0,63}\\z";
    private static readonly Regex CodeSyntax = new(CodePattern, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    public static bool ValidCode(string? code) => code is not null && CodeSyntax.IsMatch(code);
    public static bool ValidQuantity(decimal delta, string? type) => delta != 0 && delta >= -QuantityMaximum && delta <= QuantityMaximum
        && decimal.Round(delta, QuantityScale) == delta
        && (type == Receipt && delta > 0 || type == Issue && delta < 0 || type == Adjustment);
}

public sealed record Warehouse
{
    public Guid Id { get; }
    public string Code { get; }
    public string Name { get; }

    private Warehouse(Guid id, string code, string name) =>
        (Id, Code, Name) = (id, code, name);

    public static Warehouse? Create(Guid id, string? code, string? name) =>
        id != Guid.Empty && InventoryRules.ValidCode(code) && CatalogueLabel.Normalize(name) is { } label
            ? new(id, code!, label) : null;
}

public sealed record Location
{
    public Guid Id { get; }
    public Guid WarehouseId { get; }
    public string Code { get; }
    public bool Enabled { get; }

    private Location(Guid id, Guid warehouseId, string code, bool enabled) =>
        (Id, WarehouseId, Code, Enabled) = (id, warehouseId, code, enabled);

    public static Location? Create(Guid id, Guid warehouseId, string? code, bool enabled) =>
        id != Guid.Empty && warehouseId != Guid.Empty && InventoryRules.ValidCode(code)
            ? new(id, warehouseId, code!, enabled) : null;
}

public sealed record StockItem
{
    public Guid Id { get; }
    public string Sku { get; }
    public string Description { get; }
    public Guid BaseUomId { get; }
    public Guid? ItemFamilyId { get; }

    private StockItem(Guid id, string sku, string description, Guid baseUomId, Guid? itemFamilyId) =>
        (Id, Sku, Description, BaseUomId, ItemFamilyId) = (id, sku, description, baseUomId, itemFamilyId);

    public static StockItem? Create(Guid id, string? sku, string? description, Guid baseUomId, Guid? itemFamilyId) =>
        id != Guid.Empty && baseUomId != Guid.Empty && itemFamilyId != Guid.Empty
        && InventoryRules.ValidCode(sku) && CatalogueLabel.Normalize(description) is { } label
            ? new(id, sku!, label, baseUomId, itemFamilyId) : null;
}

public sealed record InventoryMovement
{
    public Guid Id { get; }
    public Guid WarehouseId { get; }
    public Guid ItemId { get; }
    public Guid LocationId { get; }
    public DateTimeOffset OccurredAt { get; }
    public decimal QuantityDelta { get; }
    public Guid UomId { get; }
    public string Type { get; }
    public string ExternalKey { get; }

    private InventoryMovement(Guid id, Guid warehouseId, Guid itemId, Guid locationId, DateTimeOffset occurredAt, decimal quantityDelta, Guid uomId, string type, string externalKey) =>
        (Id, WarehouseId, ItemId, LocationId, OccurredAt, QuantityDelta, UomId, Type, ExternalKey) = (id, warehouseId, itemId, locationId, occurredAt, quantityDelta, uomId, type, externalKey);

    public bool SameFactAs(InventoryMovement other) =>
        (WarehouseId, ItemId, LocationId, OccurredAt, QuantityDelta, UomId, Type, ExternalKey)
        == (other.WarehouseId, other.ItemId, other.LocationId, other.OccurredAt, other.QuantityDelta, other.UomId, other.Type, other.ExternalKey);

    public static InventoryMovement? Create(Guid id, Guid warehouseId, Guid itemId, Guid locationId,
        DateTimeOffset occurredAt, decimal quantityDelta, Guid uomId, string? type, string? externalKey) =>
        id != Guid.Empty && warehouseId != Guid.Empty && itemId != Guid.Empty && locationId != Guid.Empty
        && uomId != Guid.Empty && occurredAt != default && occurredAt.Ticks % TimeSpan.TicksPerMicrosecond == 0
        && InventoryRules.ValidCode(externalKey) && InventoryRules.ValidQuantity(quantityDelta, type)
            ? new(id, warehouseId, itemId, locationId, occurredAt.ToUniversalTime(), quantityDelta, uomId, type!, externalKey!) : null;
}

public sealed record InventoryBalance(Guid WarehouseId, Guid ItemId, Guid LocationId, Guid UomId, decimal Quantity);
