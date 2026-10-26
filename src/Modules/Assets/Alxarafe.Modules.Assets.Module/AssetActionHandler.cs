using Alxarafe.Resources.Runtime;
using Microsoft.EntityFrameworkCore;

namespace Alxarafe.Modules.Assets;

public sealed class AssetActionHandler(string actionName, IDbContextFactory<AssetsDbContext> contexts) : IResourceActionHandler
{
    public string ResourceId => AssetResources.Asset;
    public string ActionName => actionName;
    public async Task<ResourceActionOutcome> ExecuteAsync(ResourceActionExecutionContext context, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await AssetsPersistence.Mutation(db, cancellationToken);
        var id = AssetsPersistence.Identifier(((ResourceEntityTarget)context.Context.Target).Identifier);
        var row = await db.Assets.SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (row is null) return new(ResourceActionOutcomeKind.NotFound);
        AssetRules.Writable(row);
        if (ActionName == AssetActions.Complete)
        {
            await AssetDynamicFields.RequireComplete(db, row, cancellationToken);
            AssetRules.Complete(row, context.Context.Definition);
            await AssetRepository.ValidateReferences(db, row, AssetsRecords.Asset(row), cancellationToken);
        }
        else if (ActionName == AssetActions.Retire)
        {
            var status = await db.Statuses.SingleAsync(status => status.Id == row.StatusId, cancellationToken);
            await AssetDynamicFields.RequireComplete(db, row, cancellationToken);
            AssetRules.Retire(row, status);
            row.StatusId = (await db.Statuses.SingleAsync(status => status.Code == AssetStatusCodes.Retired, cancellationToken)).Id;
        }
        else throw new InvalidOperationException("Unknown Assets action binding.");
        row.UpdatedAt = AssetsPersistence.Now();
        await AssetsPersistence.Save(db, cancellationToken); await transaction.CommitAsync(cancellationToken);
        return new(ResourceActionOutcomeKind.Success);
    }
}
