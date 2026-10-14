using System.Data;
using System.Numerics;
using Alxarafe.Modules.Wms.Application;
using Alxarafe.Modules.Wms.Domain;
using Microsoft.EntityFrameworkCore;

namespace Alxarafe.Modules.Wms.Infrastructure;

public sealed class EfUomStore(IDbContextFactory<WmsDbContext> factory) : IUomStore
{
    private const string Columns = WmsStorage.IdColumn + ", " + WmsStorage.CodeColumn + ", " + WmsStorage.DescriptionColumn;
    private const string InsertSql = "INSERT INTO " + WmsStorage.Table + " (" + Columns
        + ") VALUES ({0}, {1}, {2}) ON CONFLICT (" + WmsStorage.CodeColumn + ") DO NOTHING";
    private const string PageSql = "SELECT " + Columns + " FROM " + WmsStorage.Table
        + " ORDER BY " + WmsStorage.CodeColumn + " ASC LIMIT {0} OFFSET {1}";
    public async Task<bool> InsertIfCodeAvailableAsync(Uom uom, CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        // The persistent unique index is the race authority, not a preflight SELECT.
        return await context.Database.ExecuteSqlRawAsync(InsertSql,
            [uom.Id, uom.Code, uom.Description], cancellationToken) == 1;
    }

    public async Task<UomWindow> ListAsync(BigInteger offset, int limit, CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        await using var snapshot = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        long total = await context.Uoms.LongCountAsync(cancellationToken);
        var rows = offset >= total ? [] : await context.Uoms.FromSqlRaw(PageSql, limit, (long)offset)
            .AsNoTracking().ToArrayAsync(cancellationToken);
        await snapshot.CommitAsync(cancellationToken);
        return new(rows.Select(row => Uom.Create(row.Id, row.Code, row.Description)
            ?? throw new InvalidOperationException("Invalid persisted unit of measure.")).ToArray(), total);
    }
}
