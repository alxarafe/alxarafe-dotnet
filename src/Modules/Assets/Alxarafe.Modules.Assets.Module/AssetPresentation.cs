using Alxarafe.Resources;

namespace Alxarafe.Modules.Assets;

// Static presentation only. Domain rules and effective authorization stay in
// their existing definitions/handlers; placement never grants an operation.
internal static class AssetPresentation
{
    internal static ResourceContextPresentationDefinition Contextual => new(
        list: new([new(AssetFields.Name), new(AssetFields.AssetTypeId), new(AssetFields.AssetTag),
            new(AssetFields.Lifecycle), new(AssetFields.Condition), new(AssetFields.LocationId)]),
        create: new([new("general", [new("identity", [new("identity", [AssetFields.AssetTypeId, AssetFields.Name], "Identity")], "Identification")], "General")]),
        edit: Definition.Form,
        view: Definition.Form);

    internal static ResourcePresentationDefinition Definition => new(new(
    [new("general",
        [new("identity", [new("identity", [AssetFields.Name, AssetFields.AssetTypeId], "Identity"),
            new("identifiers", [AssetFields.AssetTag, AssetFields.SerialNumber], "Identifiers")], "Identification"),
         new("model", [new("model", [AssetFields.ManufacturerId, AssetFields.AssetModelId], "Manufacturer and model")], "Equipment"),
         new("state", [new("state", [AssetFields.Lifecycle, AssetFields.StatusId, AssetFields.Condition], "State and condition")], "Lifecycle", actions: [AssetActions.Retire])], "General"),
     new("purchase",
        [new("purchase", [new("purchase", [AssetFields.PurchaseDate, AssetFields.PurchasePrice], "Purchase details")], "Acquisition"),
         new("notes", [new("notes", [AssetFields.Notes], "Notes")], "Additional information", collapsible: true)], "Purchase and notes"),
     new("location",
        [new("physical", [new("base", [AssetFields.LocationId], "Physical/base location")], "Location")], "Location and custody", actions: [AssetActions.Assign, AssetActions.Unassign, AssetActions.Transfer]),
     new("audit",
        [new("audit", [new("identity", [AssetFields.Id], "Record identifier"),
            new("timestamps", [AssetFields.CreatedAt, AssetFields.UpdatedAt], "Timestamps")], "Audit details", collapsible: true, initiallyCollapsed: true)], "Audit")], actions: [AssetActions.Complete]));
}
