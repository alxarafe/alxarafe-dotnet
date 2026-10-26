using Alxarafe.Resources;
using Alxarafe.Resources.Runtime;
using Microsoft.EntityFrameworkCore;

namespace Alxarafe.Modules.Assets;

public sealed class AssetAssignmentActionHandler(string actionName, IDbContextFactory<AssetsDbContext> contexts) : IResourceActionHandler
{
    public string ResourceId => AssetResources.Asset;
    public string ActionName => actionName;
    public async Task<ResourceActionOutcome> ExecuteAsync(ResourceActionExecutionContext context, CancellationToken cancellationToken = default)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await AssetsPersistence.Mutation(db, cancellationToken);
        var id = AssetsPersistence.Identifier(((ResourceEntityTarget)context.Context.Target).Identifier);
        var asset = await db.Assets.SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (asset is null) return new(ResourceActionOutcomeKind.NotFound);
        AssetRules.Writable(asset);
        if (asset.Lifecycle != AssetLifecycle.Ready) throw new ResourceConflictException(AssetFailures.InvalidTransition);
        var current = await db.Assignments.SingleOrDefaultAsync(row => row.AssetId == id && row.EndedAt == null, cancellationToken);
        if (ActionName == AssetActions.Assign && current is not null) throw new ResourceConflictException(AssetFailures.AlreadyAssigned);
        if (ActionName is AssetActions.Unassign or AssetActions.Transfer && current is null) throw new ResourceConflictException(AssetFailures.NotAssigned);

        Guid? targetId = null;
        string? targetType = null;
        if (ActionName is AssetActions.Assign or AssetActions.Transfer)
        {
            var status = await db.Statuses.SingleAsync(row => row.Id == asset.StatusId, cancellationToken);
            if (status.Code is not (AssetStatusCodes.Active or AssetStatusCodes.InStock)) throw new ResourceConflictException(AssetFailures.InvalidTransition);
            targetId = ((UuidValue)context.Input.Values[AssetFields.TargetId]).Value;
            targetType = ((TextValue)context.Input.Values[AssetFields.TargetType]).Value;
            var location = await db.Locations.SingleOrDefaultAsync(row => row.Id == targetId, cancellationToken);
            if (location is null) return new(ResourceActionOutcomeKind.NotFound);
            if (!location.Active) throw new ResourceConflictException(AssetFailures.Inactive);
            if (current is not null && current.TargetType == targetType && current.TargetId == targetId)
                throw new ResourceConflictException(AssetFailures.SameAssignmentTarget);
        }
        var now = AssetsPersistence.Now();
        var lastEnd = await db.Assignments.Where(row => row.AssetId == id && row.EndedAt != null).MaxAsync(row => row.EndedAt, cancellationToken);
        if ((current is not null && now < current.StartedAt) || (lastEnd is { } end && now < end))
            throw new ResourceConflictException(AssetFailures.AssignmentChronology);
        string? reason = Text(context.Input, AssetFields.Reason);
        if (current is not null)
        {
            current.EndedAt = current.UpdatedAt = now; current.EndReason = reason;
            // Flush the closed row before adding its successor: partial uniqueness
            // and exclusion constraints remain valid at every statement boundary.
            await AssetsPersistence.Save(db, cancellationToken);
        }
        if (ActionName != AssetActions.Unassign)
        {
            db.Assignments.Add(new AssetAssignmentRow { Id = Guid.NewGuid(), AssetId = id, TargetType = targetType!, TargetId = targetId!.Value,
                StartedAt = now, StartReason = reason, Notes = Text(context.Input, AssetFields.Notes), CreatedAt = now, UpdatedAt = now });
        }
        asset.UpdatedAt = now;
        await AssetsPersistence.Save(db, cancellationToken); await transaction.CommitAsync(cancellationToken);
        return new(ResourceActionOutcomeKind.Success);
    }
    private static string? Text(ResourceActionInput input, string field) => input.Values.TryGetValue(field, out var value) ? ((TextValue)value).Value : null;
}
