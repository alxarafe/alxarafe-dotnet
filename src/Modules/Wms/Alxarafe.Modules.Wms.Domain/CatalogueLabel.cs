using System.Buffers;
using System.Text;

namespace Alxarafe.Modules.Wms.Domain;

internal static class CatalogueLabel
{
    internal const int Maximum = 255;
    private static readonly char[] BoundaryWhitespace = [' ', '\t', '\r', '\n'];

    internal static string? Normalize(string? value)
    {
        if (value is null) return null;
        string normalized = value.Trim(BoundaryWhitespace);
        var remaining = normalized.AsSpan();
        int scalars = 0;
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out var rune, out int consumed) != OperationStatus.Done || rune.Value == 0)
                return null;
            scalars++;
            if (scalars > Maximum) return null;
            remaining = remaining[consumed..];
        }
        return scalars == 0 ? null : normalized;
    }
}
