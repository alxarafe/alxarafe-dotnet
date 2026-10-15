using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Alxarafe.Modules.Wms.Domain;

[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix",
    Justification = "Storage Attribute is the published WMS domain term, not a CLR annotation.")]
public sealed record StorageAttribute
{
    public const int NameMaximum = CatalogueLabel.Maximum;
    public const int CodeMaximum = 30;
    public const string CodePattern = "^[A-Z][A-Z0-9_]{0,29}$";
    private static readonly Regex CodeSyntax = new(CodePattern, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    public Guid Id { get; }
    public string Code { get; }
    public string Name { get; }

    private StorageAttribute(Guid id, string code, string name) => (Id, Code, Name) = (id, code, name);

    public static StorageAttribute? Create(Guid id, string? code, string? name)
    {
        if (id == Guid.Empty || code is null || name is null) return null;
        var match = CodeSyntax.Match(code);
        // .NET's $ also matches before a final LF; the contract requires the entire code.
        if (!match.Success || match.Length != code.Length) return null;
        string? normalized = CatalogueLabel.Normalize(name);
        return normalized is null ? null : new StorageAttribute(id, code, normalized);
    }
}
