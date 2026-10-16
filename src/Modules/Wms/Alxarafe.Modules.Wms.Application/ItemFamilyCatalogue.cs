using System.Numerics;
using Alxarafe.Modules.Wms.Domain;

namespace Alxarafe.Modules.Wms.Application;

public interface IItemFamilyStore
{
    Task<bool> InsertIfCodeAvailableAsync(ItemFamily family, CancellationToken cancellationToken = default);
    Task<ItemFamilyWindow> ListAsync(BigInteger offset, int limit, CancellationToken cancellationToken = default);
}
public sealed record ItemFamilyWindow(IReadOnlyList<ItemFamily> Items, long Total);
public enum CreateItemFamilyOutcome { Created, InvalidRequest, CodeConflict }
public sealed record CreateItemFamilyResult(CreateItemFamilyOutcome Outcome, ItemFamily? ItemFamily = null);

public sealed class ItemFamilyCatalogue(IItemFamilyStore store)
{
    public async Task<CreateItemFamilyResult> CreateAsync(string? code, string? name, CancellationToken cancellationToken = default)
    {
        var family = ItemFamily.Create(Guid.NewGuid(), code, name);
        if (family is null) return new(CreateItemFamilyOutcome.InvalidRequest);
        return await store.InsertIfCodeAvailableAsync(family, cancellationToken)
            ? new(CreateItemFamilyOutcome.Created, family) : new(CreateItemFamilyOutcome.CodeConflict);
    }

    public Task<ItemFamilyWindow> ListAsync(BigInteger offset, int limit, CancellationToken cancellationToken = default) =>
        store.ListAsync(offset, limit, cancellationToken);
}
