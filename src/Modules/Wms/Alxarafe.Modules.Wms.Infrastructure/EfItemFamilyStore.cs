using System.Data;
using System.Numerics;
using Alxarafe.Modules.Wms.Application;
using Alxarafe.Modules.Wms.Domain;
using Microsoft.EntityFrameworkCore;

namespace Alxarafe.Modules.Wms.Infrastructure;

public sealed class EfItemFamilyStore(IDbContextFactory<WmsDbContext> factory) : IItemFamilyStore
{
    private const string Columns = WmsStorage.IdColumn + ", " + WmsStorage.CodeColumn + ", " + WmsStorage.NameColumn;
    private const string InsertSql = "INSERT INTO " + WmsStorage.ItemFamilyTable + " (" + Columns
        + ") VALUES ({0}, {1}, {2}) ON CONFLICT (" + WmsStorage.CodeColumn + ") DO NOTHING";
    private const string PageSql = "SELECT " + Columns + " FROM " + WmsStorage.ItemFamilyTable
        + " ORDER BY " + WmsStorage.CodeColumn + " ASC LIMIT {0} OFFSET {1}";
    public async Task<bool> InsertIfCodeAvailableAsync(ItemFamily family, CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        // The persistent unique index is the race authority, not a preflight SELECT.
        return await context.Database.ExecuteSqlRawAsync(InsertSql,
            [family.Id, family.Code, family.Name], cancellationToken) == 1;
    }

    public async Task<ItemFamilyWindow> ListAsync(BigInteger offset, int limit, CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        await using var snapshot = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        long total = await context.ItemFamilies.LongCountAsync(cancellationToken);
        var rows = offset >= total ? [] : await context.ItemFamilies.FromSqlRaw(PageSql, limit, (long)offset)
            .AsNoTracking().ToArrayAsync(cancellationToken);
        await snapshot.CommitAsync(cancellationToken);
        return new(rows.Select(row => ItemFamily.Create(row.Id, row.Code, row.Name)
            ?? throw new InvalidOperationException("Invalid persisted item family.")).ToArray(), total);
    }
}
