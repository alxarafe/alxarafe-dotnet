using System.Text.RegularExpressions;

namespace Alxarafe.Modules.Wms.Domain;

internal static class CatalogueCode
{
    public const int Maximum = 30;
    public const string Pattern = "^[A-Z][A-Z0-9_]{0,29}$";
    private static readonly Regex Syntax = new(Pattern, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static bool IsValid(string value)
    {
        var match = Syntax.Match(value);
        // .NET's $ may match before a final LF; require the entire input.
        return match.Success && match.Length == value.Length;
    }
}
