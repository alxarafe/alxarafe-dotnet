using System.Buffers;
using System.Text;
using System.Text.RegularExpressions;

namespace Alxarafe.Modules.Wms.Domain;

public sealed record Uom
{
    public const int DescriptionMaximum = 255;
    private static readonly char[] BoundaryWhitespace = [' ', '\t', '\r', '\n'];
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
        string normalized = description.Trim(BoundaryWhitespace);
        var remaining = normalized.AsSpan();
        int scalars = 0;
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out var rune, out int consumed) != OperationStatus.Done || rune.Value == 0)
                return null;
            scalars++;
            if (scalars > DescriptionMaximum) return null;
            remaining = remaining[consumed..];
        }
        return scalars == 0 ? null : new Uom(id, code, normalized);
    }
}
