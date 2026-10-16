using System.Diagnostics.CodeAnalysis;

namespace Alxarafe.Modules.Wms.Domain;

[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix",
    Justification = "Storage Attribute is the published WMS domain term, not a CLR annotation.")]
public sealed record StorageAttribute
{
    public const int NameMaximum = CatalogueLabel.Maximum;
    public const int CodeMaximum = CatalogueCode.Maximum;
    public const string CodePattern = CatalogueCode.Pattern;
    public Guid Id { get; }
    public string Code { get; }
    public string Name { get; }

    private StorageAttribute(Guid id, string code, string name) => (Id, Code, Name) = (id, code, name);

    public static StorageAttribute? Create(Guid id, string? code, string? name)
    {
        if (id == Guid.Empty || code is null || name is null) return null;
        if (!CatalogueCode.IsValid(code)) return null;
        string? normalized = CatalogueLabel.Normalize(name);
        return normalized is null ? null : new StorageAttribute(id, code, normalized);
    }
}
