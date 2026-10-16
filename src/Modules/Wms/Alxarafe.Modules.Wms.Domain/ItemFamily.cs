
namespace Alxarafe.Modules.Wms.Domain;

public sealed record ItemFamily
{
    public const int NameMaximum = CatalogueLabel.Maximum;
    public const int CodeMaximum = CatalogueCode.Maximum;
    public const string CodePattern = CatalogueCode.Pattern;
    public Guid Id { get; }
    public string Code { get; }
    public string Name { get; }

    private ItemFamily(Guid id, string code, string name) => (Id, Code, Name) = (id, code, name);

    public static ItemFamily? Create(Guid id, string? code, string? name)
    {
        if (id == Guid.Empty || code is null || name is null) return null;
        if (!CatalogueCode.IsValid(code)) return null;
        string? normalized = CatalogueLabel.Normalize(name);
        return normalized is null ? null : new ItemFamily(id, code, normalized);
    }
}
