using System.Data;
using Alxarafe.Modules.Wms.Application;
using Alxarafe.Modules.Wms.Domain;
using Microsoft.EntityFrameworkCore;

namespace Alxarafe.Modules.Wms.Infrastructure;

public sealed class EfItemFamilyAssociationStore(IDbContextFactory<WmsDbContext> factory) : IItemFamilyAssociationStore
{
    private const string LockFamilySql = "SELECT * FROM " + WmsStorage.ItemFamilyTable
        + " WHERE " + WmsStorage.IdColumn + " = {0} FOR UPDATE";

    private const string PageSql = "SELECT attributes.* FROM " + WmsStorage.StorageAttributeTable + " attributes JOIN "
        + WmsStorage.AssociationTable + " pairs ON attributes." + WmsStorage.IdColumn + " = pairs." + WmsStorage.AttributeIdColumn
        + " WHERE pairs." + WmsStorage.FamilyIdColumn + " = {0} ORDER BY attributes." + WmsStorage.CodeColumn + " ASC LIMIT {1} OFFSET {2}";

    public async Task<AssociationResult> ExecuteAsync(Guid familyId, AssociationCommand command, CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        bool reading = command.Operation == AssociationOperation.List;
        await using var transaction = await context.Database.BeginTransactionAsync(
            reading ? IsolationLevel.RepeatableRead : IsolationLevel.ReadCommitted, cancellationToken);
        // Every mutation locks the same existing family before reading relation state. A competing
        // writer then sees the committed predecessor, including an association POST race.
        bool familyExists = reading
            ? await context.ItemFamilies.AnyAsync(family => family.Id == familyId, cancellationToken)
            : (await context.ItemFamilies.FromSqlRaw(LockFamilySql, familyId).AsNoTracking().ToArrayAsync(cancellationToken)).Length != 0;
        var references = command.Add.Concat(command.Remove).Select(reference => reference.Id).Distinct().ToArray();
        var masters = await context.StorageAttributes.Where(attribute => references.Contains(attribute.Id)).AsNoTracking().ToArrayAsync(cancellationToken);
        var current = await context.ItemFamilyAssociations.Where(pair => pair.ItemFamilyId == familyId)
            .Select(pair => pair.StorageAttributeId).ToArrayAsync(cancellationToken);
        var failures = ItemFamilyAssociations.Validate(familyId, familyExists, command,
            masters.Select(attribute => attribute.Id).ToHashSet(), current.ToHashSet());
        if (failures.Count != 0) return new(failures, []);
        if (reading)
        {
            var query = context.StorageAttributes.Where(attribute => current.Contains(attribute.Id));
            long total = await query.LongCountAsync(cancellationToken);
            var rows = command.Offset >= total ? [] : await context.StorageAttributes.FromSqlRaw(PageSql, familyId, command.Limit, (long)command.Offset)
                .AsNoTracking().ToArrayAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new([], rows.Select(ToDomain).ToArray(), total);
        }
        var desired = command.Operation == AssociationOperation.Replace ? command.Add.Select(reference => reference.Id).ToHashSet() : current.ToHashSet();
        if (command.Operation != AssociationOperation.Replace)
        {
            desired.UnionWith(command.Add.Select(reference => reference.Id));
            desired.ExceptWith(command.Remove.Select(reference => reference.Id));
        }
        var removed = current.Where(id => !desired.Contains(id)).ToArray();
        await context.ItemFamilyAssociations.Where(pair => pair.ItemFamilyId == familyId && removed.Contains(pair.StorageAttributeId))
            .ExecuteDeleteAsync(cancellationToken);
        var old = current.ToHashSet();
        context.ItemFamilyAssociations.AddRange(desired.Where(id => !old.Contains(id))
            .Select(id => new ItemFamilyAssociationRow { ItemFamilyId = familyId, StorageAttributeId = id }));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new([], command.Operation == AssociationOperation.Create ? masters.Select(ToDomain).ToArray() : []);
    }

    private static StorageAttribute ToDomain(StorageAttributeRow row) => StorageAttribute.Create(row.Id, row.Code, row.Name)
        ?? throw new InvalidOperationException("Invalid persisted storage attribute.");
}
