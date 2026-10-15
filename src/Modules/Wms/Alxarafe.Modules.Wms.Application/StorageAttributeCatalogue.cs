using System.Numerics;
using Alxarafe.Modules.Wms.Domain;

namespace Alxarafe.Modules.Wms.Application;

public interface IStorageAttributeStore
{
    Task<bool> InsertIfCodeAvailableAsync(StorageAttribute attribute, CancellationToken cancellationToken = default);
    Task<StorageAttributeWindow> ListAsync(BigInteger offset, int limit, CancellationToken cancellationToken = default);
}
public sealed record StorageAttributeWindow(IReadOnlyList<StorageAttribute> Items, long Total);
public enum CreateStorageAttributeOutcome { Created, InvalidRequest, CodeConflict }
public sealed record CreateStorageAttributeResult(CreateStorageAttributeOutcome Outcome, StorageAttribute? StorageAttribute = null);

public sealed class StorageAttributeCatalogue(IStorageAttributeStore store)
{
    public async Task<CreateStorageAttributeResult> CreateAsync(string? code, string? name, CancellationToken cancellationToken = default)
    {
        var attribute = StorageAttribute.Create(Guid.NewGuid(), code, name);
        if (attribute is null) return new(CreateStorageAttributeOutcome.InvalidRequest);
        return await store.InsertIfCodeAvailableAsync(attribute, cancellationToken)
            ? new(CreateStorageAttributeOutcome.Created, attribute) : new(CreateStorageAttributeOutcome.CodeConflict);
    }

    public Task<StorageAttributeWindow> ListAsync(BigInteger offset, int limit, CancellationToken cancellationToken = default) =>
        store.ListAsync(offset, limit, cancellationToken);
}
