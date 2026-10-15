using System.Data;
using System.Numerics;
using Alxarafe.Modules.Wms.Application;
using Alxarafe.Modules.Wms.Domain;
using Microsoft.EntityFrameworkCore;

namespace Alxarafe.Modules.Wms.Infrastructure;

public sealed class EfStorageAttributeStore(IDbContextFactory<WmsDbContext> factory) : IStorageAttributeStore
{
    private const string Columns = WmsStorage.IdColumn + ", " + WmsStorage.CodeColumn + ", " + WmsStorage.NameColumn;
    private const string InsertSql = "INSERT INTO " + WmsStorage.StorageAttributeTable + " (" + Columns
        + ") VALUES ({0}, {1}, {2}) ON CONFLICT (" + WmsStorage.CodeColumn + ") DO NOTHING";
    private const string PageSql = "SELECT " + Columns + " FROM " + WmsStorage.StorageAttributeTable
        + " ORDER BY " + WmsStorage.CodeColumn + " ASC LIMIT {0} OFFSET {1}";
    public async Task<bool> InsertIfCodeAvailableAsync(StorageAttribute attribute, CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        // The persistent unique index is the race authority, not a preflight SELECT.
        return await context.Database.ExecuteSqlRawAsync(InsertSql,
            [attribute.Id, attribute.Code, attribute.Name], cancellationToken) == 1;
    }

    public async Task<StorageAttributeWindow> ListAsync(BigInteger offset, int limit, CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        await using var snapshot = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        long total = await context.StorageAttributes.LongCountAsync(cancellationToken);
        var rows = offset >= total ? [] : await context.StorageAttributes.FromSqlRaw(PageSql, limit, (long)offset)
            .AsNoTracking().ToArrayAsync(cancellationToken);
        await snapshot.CommitAsync(cancellationToken);
        return new(rows.Select(row => StorageAttribute.Create(row.Id, row.Code, row.Name)
            ?? throw new InvalidOperationException("Invalid persisted storage attribute.")).ToArray(), total);
    }
}
