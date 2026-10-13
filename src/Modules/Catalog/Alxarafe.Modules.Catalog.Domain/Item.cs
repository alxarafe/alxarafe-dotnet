namespace Alxarafe.Modules.Catalog.Domain;

public readonly record struct ItemId(Guid Value)
{
    public static ItemId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

public sealed record Sku
{
    public const int MaximumLength = 50;
    public const string Field = "sku";
    public string Value { get; }
    private Sku(string value) => Value = value;
    public static Sku Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumLength) throw new ItemValidationException(Field, MaximumLength);
        return new Sku(value.Trim().ToUpperInvariant());
    }
}

public sealed record ItemName
{
    public const int MaximumLength = 200;
    public const string Field = "name";
    public string Value { get; }
    private ItemName(string value) => Value = value;
    public static ItemName Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > MaximumLength) throw new ItemValidationException(Field, MaximumLength);
        return new ItemName(value.Trim());
    }
}

public sealed class Item
{
    public ItemId Id { get; }
    public Sku Sku { get; }
    public ItemName Name { get; }

    private Item(ItemId id, Sku sku, ItemName name) => (Id, Sku, Name) = (id, sku, name);
    public static Item Create(Sku sku, ItemName name) => new(ItemId.New(), sku, name);
    public static Item Rehydrate(ItemId id, Sku sku, ItemName name) => new(id, sku, name);
}
