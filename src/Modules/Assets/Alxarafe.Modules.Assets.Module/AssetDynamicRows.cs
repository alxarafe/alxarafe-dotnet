using Alxarafe.Resources;

namespace Alxarafe.Modules.Assets;

public sealed class AssetFieldDefinitionRow
{
    public Guid Id { get; set; }
    public Guid AssetTypeId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public ScalarType FieldType { get; set; }
    public bool Required { get; set; }
    public bool Active { get; set; } = true;
    public int DisplayOrder { get; set; }
    public int? MinimumLength { get; set; }
    public int? MaximumLength { get; set; }
    // Exact ResourceNumber canonical text, never CLR double/decimal coercion.
    public string? Minimum { get; set; }
    public string? Maximum { get; set; }
    public int? Precision { get; set; }
    public int? Scale { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AssetFieldValueRow
{
    public Guid Id { get; set; }
    public Guid AssetId { get; set; }
    public Guid FieldDefinitionId { get; set; }
    // Derived from the asset; the two composite FKs enforce matching types.
    public Guid AssetTypeId { get; set; }
    public string ValueJson { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public AssetFieldDefinitionRow Definition { get; set; } = null!;
}
