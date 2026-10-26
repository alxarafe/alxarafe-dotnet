namespace Alxarafe.Modules.Assets;

public abstract class AssetCatalogueRow
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AssetTypeRow : AssetCatalogueRow
{
    public string? Description { get; set; }
}
public sealed class ManufacturerRow : AssetCatalogueRow;
public sealed class AssetModelRow : AssetCatalogueRow
{
    public Guid ManufacturerId { get; set; }
}
public sealed class AssetLocationRow : AssetCatalogueRow;
public sealed class AssetStatusRow : AssetCatalogueRow;

public sealed class AssetRow
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public Guid AssetTypeId { get; set; }
    public string? AssetTag { get; set; }
    public string? SerialNumber { get; set; }
    public Guid? ManufacturerId { get; set; }
    public Guid? AssetModelId { get; set; }
    public Guid? LocationId { get; set; }
    public Guid StatusId { get; set; }
    public string Lifecycle { get; set; } = AssetLifecycle.Draft;
    public DateOnly? PurchaseDate { get; set; }
    public decimal? PurchasePrice { get; set; }
    public byte? Condition { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
