using System.Data;
using Alxarafe.Modularity;
using Microsoft.EntityFrameworkCore;

namespace Alxarafe.EntityFrameworkCore;

public sealed class EfModuleStateStore(IDbContextFactory<PlatformDbContext> factory) : IModuleStateStore
{
    public async Task<bool?> FindAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await context.ModuleStates.AsNoTracking().Where(state => state.ModuleId == moduleId)
            .Select(state => (bool?)state.Enabled).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task UpdateAsync(Action<IDictionary<string, bool>> change, CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        // One small platform table; lock before any snapshot or insertion.
        await context.Database.ExecuteSqlRawAsync("LOCK TABLE " + PlatformStorage.ModuleStatesTable + " IN SHARE ROW EXCLUSIVE MODE", cancellationToken);
        var rows = await context.ModuleStates.ToDictionaryAsync(state => state.ModuleId, StringComparer.Ordinal, cancellationToken);
        var states = rows.ToDictionary(pair => pair.Key, pair => pair.Value.Enabled, StringComparer.Ordinal);
        change(states);
        foreach (var state in states.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (rows.TryGetValue(state.Key, out var row))
                row.Enabled = state.Value;
            else
                context.ModuleStates.Add(new ModuleStateRow { ModuleId = state.Key, Enabled = state.Value });
        }
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
