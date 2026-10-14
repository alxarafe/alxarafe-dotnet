using System.Numerics;
using Alxarafe.Modules.Wms.Domain;

namespace Alxarafe.Modules.Wms.Application;

public interface IUomStore
{
    Task<bool> InsertIfCodeAvailableAsync(Uom uom, CancellationToken cancellationToken = default);
    Task<UomWindow> ListAsync(BigInteger offset, int limit, CancellationToken cancellationToken = default);
}
public sealed record UomWindow(IReadOnlyList<Uom> Items, long Total);
public enum CreateUomOutcome { Created, InvalidRequest, CodeConflict }
public sealed record CreateUomResult(CreateUomOutcome Outcome, Uom? Uom = null);

public sealed class UomCatalogue(IUomStore store)
{
    public async Task<CreateUomResult> CreateAsync(string? code, string? description, CancellationToken cancellationToken = default)
    {
        var uom = Uom.Create(Guid.NewGuid(), code, description);
        if (uom is null) return new(CreateUomOutcome.InvalidRequest);
        return await store.InsertIfCodeAvailableAsync(uom, cancellationToken)
            ? new(CreateUomOutcome.Created, uom) : new(CreateUomOutcome.CodeConflict);
    }

    public Task<UomWindow> ListAsync(BigInteger offset, int limit, CancellationToken cancellationToken = default) =>
        store.ListAsync(offset, limit, cancellationToken);
}
