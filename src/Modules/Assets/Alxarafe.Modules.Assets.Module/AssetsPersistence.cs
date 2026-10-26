using System.Globalization;
using System.Linq.Expressions;
using Alxarafe.Resources;
using Alxarafe.Resources.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Alxarafe.Modules.Assets;

internal static class AssetsPersistence
{
    internal static DateTimeOffset Now()
    {
        var now = DateTimeOffset.UtcNow;
        return new(now.Ticks - now.Ticks % TimeSpan.TicksPerMicrosecond, TimeSpan.Zero);
    }
    internal static async Task<IDbContextTransaction> Mutation(AssetsDbContext db, CancellationToken ct)
    {
        var transaction = await db.Database.BeginTransactionAsync(ct);
        try { await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({AssetsStorage.MutationLock})", ct); return transaction; }
        catch { await transaction.DisposeAsync(); throw; }
    }
    internal static async Task Save(AssetsDbContext db, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException failure) when (failure.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new ResourceConflictException(AssetFailures.Duplicate); }
        catch (DbUpdateException failure) when (failure.InnerException is PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation })
        { throw new ResourceConflictException(AssetFailures.AssignmentOverlap); }
        catch (DbUpdateException failure) when (failure.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        { throw new ResourceConflictException(AssetFailures.Referenced); }
    }
    internal static async Task<T> Reference<T>(AssetsDbContext db, Guid id, string resource, bool requireActive, CancellationToken ct) where T : AssetCatalogueRow
    {
        var row = await db.Set<T>().SingleOrDefaultAsync(row => row.Id == id, ct)
            ?? throw new ResourceRuntimeException(resource, ResourceRuntimeError.ResourceNotFound);
        if (requireActive && !row.Active) throw new ResourceConflictException(AssetFailures.Inactive);
        return row;
    }
    internal static void Nonblank(string resource, string field, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ResourceRuntimeException(resource, ResourceRuntimeError.ValidationFailed,
            [new(field, ResourceInputError.InvalidValue, "Non-whitespace text is required.")]);
    }
    internal static Guid Identifier(ResourceIdentifier id) => ((UuidValue)id.Value).Value;
    internal static string Text(ResourceRecord record, string field) => ((TextValue)record.Values[field]).Value;
    internal static Guid Uuid(ResourceRecord record, string field) => ((UuidValue)record.Values[field]).Value;

    internal static async Task<ResourcePage> Page<T>(IQueryable<T> source, ResourceQuery query, Func<T, ResourceRecord> record, CancellationToken ct)
    {
        foreach (var filter in query.Filters)
        {
            var parameter = Expression.Parameter(typeof(T));
            var property = Expression.Property(parameter, Property(filter.Field));
            object? raw = filter.Value switch { UuidValue uuid => uuid.Value, TextValue text => text.Value, BooleanValue boolean => boolean.Value, DateTimeValue instant => instant.Value.ToUniversalTime(), DateValue date => date.Value, NumericValue number => Numeric(property.Type, number.Value), _ => null };
            Expression value = raw is null ? Expression.Constant(null, property.Type) : Expression.Convert(Expression.Constant(raw), property.Type);
            Expression body;
            if (filter.Operator is ResourceFilterOperator.Contains or ResourceFilterOperator.StartsWith)
                body = Expression.AndAlso(Expression.NotEqual(property, Expression.Constant(null, property.Type)),
                    Expression.Call(property, filter.Operator == ResourceFilterOperator.Contains ? nameof(string.Contains) : nameof(string.StartsWith), Type.EmptyTypes, value));
            else if (filter.Operator is ResourceFilterOperator.Equal or ResourceFilterOperator.NotEqual)
                body = filter.Operator == ResourceFilterOperator.Equal ? Expression.Equal(property, value) : Expression.NotEqual(property, value);
            else
            {
                bool text = property.Type == typeof(string);
                Expression comparison = text ? Expression.Call(typeof(string), nameof(string.Compare), Type.EmptyTypes, property, value) : property;
                Expression bound = text ? Expression.Constant(0) : value;
                body = filter.Operator switch
                {
                    ResourceFilterOperator.LessThan => Expression.LessThan(comparison, bound),
                    ResourceFilterOperator.LessThanOrEqual => Expression.LessThanOrEqual(comparison, bound),
                    ResourceFilterOperator.GreaterThan => Expression.GreaterThan(comparison, bound),
                    _ => Expression.GreaterThanOrEqual(comparison, bound)
                };
            }
            source = source.Where(Expression.Lambda<Func<T, bool>>(body, parameter));
        }
        if (query.Search is { } search)
        {
            QueryText(AssetResources.Asset, "search", search);
            // Only Asset opts into search. Other resources fail Runtime validation.
            var parameter = Expression.Parameter(typeof(T));
            Expression Match(string field)
            {
                var property = Expression.Property(parameter, Property(field));
                return Expression.AndAlso(Expression.NotEqual(property, Expression.Constant(null, typeof(string))),
                    Expression.Call(property, nameof(string.Contains), Type.EmptyTypes, Expression.Constant(search)));
            }
            source = source.Where(Expression.Lambda<Func<T, bool>>(Expression.OrElse(Match(AssetFields.Name), Match(AssetFields.AssetTag)), parameter));
        }
        long total = await source.LongCountAsync(ct);
        var sorting = query.Sorting.ToList();
        if (!sorting.Any(sort => sort.Field == AssetFields.Id)) sorting.Add(new(AssetFields.Id));
        for (int index = 0; index < sorting.Count; index++)
        {
            var sort = sorting[index]; var parameter = Expression.Parameter(typeof(T));
            var property = Expression.Property(parameter, Property(sort.Field));
            bool descending = sort.Direction == ResourceSortDirection.Descending;
            string method = index == 0
                ? (descending ? nameof(Queryable.OrderByDescending) : nameof(Queryable.OrderBy))
                : (descending ? nameof(Queryable.ThenByDescending) : nameof(Queryable.ThenBy));
            source = source.Provider.CreateQuery<T>(Expression.Call(typeof(Queryable), method, [typeof(T), property.Type], source.Expression,
                Expression.Quote(Expression.Lambda(property, parameter))));
        }
        var rows = query.Offset > int.MaxValue ? [] : await source.Skip((int)query.Offset).Take(query.Limit).ToListAsync(ct);
        return new(rows.Select(record), query.Offset, query.Limit, total);
    }
    internal static void QueryText(string resource, string field, string value)
    {
        if (value.Contains(char.MinValue, StringComparison.Ordinal))
            throw new ResourceRuntimeException(resource, ResourceRuntimeError.ValidationFailed,
                [new(field, ResourceInputError.InvalidQuery, "Query text cannot contain NUL.")]);
    }
    private static object Numeric(Type property, ResourceNumber value)
    {
        var type = Nullable.GetUnderlyingType(property) ?? property;
        // Existing static Asset numeric columns are byte/int/decimal. Dynamic
        // arbitrary-precision numbers take the JSONB path, never CLR decimal.
        return Convert.ChangeType(value.CanonicalText, type, CultureInfo.InvariantCulture);
    }
    private static string Property(string field) => char.ToUpperInvariant(field[0]) + field[1..];
}
