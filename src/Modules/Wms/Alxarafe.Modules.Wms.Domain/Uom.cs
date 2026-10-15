using System.Text.RegularExpressions;

namespace Alxarafe.Modules.Wms.Domain;

public sealed record Uom
{
    public const int DescriptionMaximum = CatalogueLabel.Maximum;
    private static readonly Regex CodeSyntax = new("^[A-Z][A-Z0-9]{0,9}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    public Guid Id { get; }
    public string Code { get; }
    public string Description { get; }

    private Uom(Guid id, string code, string description) => (Id, Code, Description) = (id, code, description);

    public static Uom? Create(Guid id, string? code, string? description)
    {
        if (id == Guid.Empty || code is null || description is null) return null;
        var match = CodeSyntax.Match(code);
        // .NET's $ also matches before a final LF; the contract requires the entire code.
        if (!match.Success || match.Length != code.Length) return null;
        string? normalized = CatalogueLabel.Normalize(description);
        return normalized is null ? null : new Uom(id, code, normalized);
    }
}
