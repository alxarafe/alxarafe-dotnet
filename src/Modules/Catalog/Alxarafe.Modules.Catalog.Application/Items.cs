using Alxarafe.Modules.Catalog.Domain;

namespace Alxarafe.Modules.Catalog.Application;

public interface IItemRepository
{
    Task AddAsync(Item item, CancellationToken cancellationToken = default);
    Task<Item?> GetByIdAsync(ItemId id, CancellationToken cancellationToken = default);
    Task<bool> ExistsBySkuAsync(Sku sku, CancellationToken cancellationToken = default);
}

public sealed record CreateItemCommand(string Sku, string Name);
public sealed record ItemDto(Guid Id, string Sku, string Name);
public sealed class ItemAlreadyExistsException(string sku) : InvalidOperationException($"An item with SKU '{sku}' already exists.")
{
    public string Sku { get; } = sku;
}

public sealed class CreateItemHandler(IItemRepository repository)
{
    public async Task<ItemDto> HandleAsync(CreateItemCommand command, CancellationToken cancellationToken = default)
    {
        var sku = Sku.Create(command.Sku);
        if (await repository.ExistsBySkuAsync(sku, cancellationToken))
            throw new ItemAlreadyExistsException(sku.Value);
        var item = Item.Create(sku, ItemName.Create(command.Name));
        await repository.AddAsync(item, cancellationToken);
        return new ItemDto(item.Id.Value, item.Sku.Value, item.Name.Value);
    }
}

public sealed class GetItemByIdHandler(IItemRepository repository)
{
    public async Task<ItemDto?> HandleAsync(ItemId id, CancellationToken cancellationToken = default)
    {
        var item = await repository.GetByIdAsync(id, cancellationToken);
        return item is null ? null : new ItemDto(item.Id.Value, item.Sku.Value, item.Name.Value);
    }
}
