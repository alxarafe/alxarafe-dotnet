using Alxarafe.Modularity;

namespace Alxarafe.Modules.Wms;

internal static class WmsCapabilities
{
    private const string Input = "#/components/schemas/CapabilityListInput";
    private const string Version = "1.0.0";
    private const string Read = "read";
    public const string UomsRead = "wms.uoms.read";
    public const string StorageAttributesRead = "wms.storage-attributes.read";
    public const string ItemFamiliesRead = "wms.item-families.read";

    private const string UomOutput = "#/components/schemas/WmsUomCollection";
    private const string StorageAttributeOutput = "#/components/schemas/WmsStorageAttributeCollection";
    private const string ItemFamilyOutput = "#/components/schemas/WmsItemFamilyCollection";
    public static IEnumerable<string> SchemaReferences => [Input, UomOutput, StorageAttributeOutput, ItemFamilyOutput];

    public static IEnumerable<CapabilityDescriptor> All =>
    [
        new("wms.uoms.list", WmsModule.Id, Version, "List units of measure",
            "GET /api/wms/uoms with optional offset and limit; returns the existing paged catalogue.", Read,
            new(Input), new(UomOutput), [UomsRead]),
        new("wms.storage-attributes.list", WmsModule.Id, Version, "List storage attributes",
            "GET /api/wms/storage-attributes with optional offset and limit; returns the existing paged catalogue.", Read,
            new(Input), new(StorageAttributeOutput), [StorageAttributesRead]),
        new("wms.item-families.list", WmsModule.Id, Version, "List item families",
            "GET /api/wms/item-families with optional offset and limit; returns the existing paged catalogue.", Read,
            new(Input), new(ItemFamilyOutput), [ItemFamiliesRead])
    ];
}
