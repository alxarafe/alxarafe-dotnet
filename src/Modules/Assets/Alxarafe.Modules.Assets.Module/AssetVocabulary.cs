namespace Alxarafe.Modules.Assets;

public static class AssetFields
{
    public const string Id = "id";
    public const string Name = "name";
    public const string Code = "code";
    public const string Active = "active";
    public const string Description = "description";
    public const string CreatedAt = "createdAt";
    public const string UpdatedAt = "updatedAt";
    public const string AssetTypeId = "assetTypeId";
    public const string AssetTag = "assetTag";
    public const string SerialNumber = "serialNumber";
    public const string ManufacturerId = "manufacturerId";
    public const string AssetModelId = "assetModelId";
    public const string LocationId = "locationId";
    public const string StatusId = "statusId";
    public const string Lifecycle = "lifecycle";
    public const string PurchaseDate = "purchaseDate";
    public const string PurchasePrice = "purchasePrice";
    public const string Condition = "condition";
    public const string Notes = "notes";
    public const string FieldType = "fieldType";
    public const string Required = "required";
    public const string DisplayOrder = "displayOrder";
    public const string MinimumLength = "minimumLength";
    public const string MaximumLength = "maximumLength";
    public const string Minimum = "minimum";
    public const string Maximum = "maximum";
    public const string Precision = "precision";
    public const string Scale = "scale";
    public const string ValueField = "valueField";
    public const string AssetId = "assetId";
    public const string FieldDefinitionId = "fieldDefinitionId";
    public const string StringValue = "stringValue";
    public const string Int32Value = "int32Value";
    public const string DecimalValue = "decimalValue";
    public const string BooleanValue = "booleanValue";
    public const string DateValue = "dateValue";
}

public static class AssetResources
{
    public const string Asset = "assets.asset";
    public const string Type = "assets.asset-type";
    public const string Manufacturer = "assets.manufacturer";
    public const string Model = "assets.asset-model";
    public const string Location = "assets.location";
    public const string Status = "assets.asset-status";
    public const string FieldDefinition = "assets.asset-field-definition";
    public const string FieldValue = "assets.asset-field-value";
    public static readonly IReadOnlyList<string> All = Array.AsReadOnly(new[] { Asset, Type, Manufacturer, Model, Location, Status, FieldDefinition, FieldValue });
}

public static class AssetLifecycle
{
    public const string Draft = "DRAFT";
    public const string Ready = "READY";
    public const string Retired = "RETIRED";
}

public static class AssetActions
{
    public const string Complete = "complete";
    public const string Retire = "retire";
}

public static class AssetStatusCodes
{
    public const string Active = "ACTIVE";
    public const string InStock = "IN_STOCK";
    public const string Assigned = "ASSIGNED";
    public const string InRepair = "IN_REPAIR";
    public const string Retired = "RETIRED";
    public static readonly IReadOnlyList<string> Reserved = Array.AsReadOnly(new[] { Active, InStock, Assigned, InRepair, Retired });
}

public static class AssetLimits
{
    public const int CodeLength = 30;
    public const int NameLength = 255;
    public const int MaximumCondition = 100;
    public const int PricePrecision = 18;
    public const int PriceScale = 2;
}

public static class AssetFailures
{
    public const string Retired = "assets_retired";
    public const string InvalidTransition = "assets_invalid_transition";
    public const string ModelManufacturer = "assets_model_manufacturer_mismatch";
    public const string Referenced = "assets_master_referenced";
    public const string Duplicate = "assets_duplicate";
    public const string Inactive = "assets_inactive_reference";
    public const string ReservedStatus = "assets_reserved_status";
    public const string FieldTypeMismatch = "assets_field_type_mismatch";
    public const string InactiveField = "assets_inactive_field";
    public const string IncompatibleDefinition = "assets_incompatible_definition";
    public const string RequiredFields = "assets_required_fields_incomplete";
    public const string RequiredValue = "assets_required_value";
}
