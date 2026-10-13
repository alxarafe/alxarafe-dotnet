namespace Alxarafe.Modules.Catalog.Domain;

/// <summary>Invariant data only; the HTTP adapter owns its localized description.</summary>
public sealed class ItemValidationException(string field, int maximumLength)
    : ArgumentException("Invalid catalog field.", field)
{
    public string Field { get; } = field;
    public int MaximumLength { get; } = maximumLength;
}
