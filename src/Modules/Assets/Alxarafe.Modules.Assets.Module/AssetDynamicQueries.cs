using System.Runtime.CompilerServices;
using Alxarafe.Resources;
using Alxarafe.Resources.AspNetCore;
using Alxarafe.Resources.Runtime;
using Microsoft.EntityFrameworkCore;

namespace Alxarafe.Modules.Assets;

internal static class AssetDynamicQueries
{
    internal static bool IsSlot(string field) => AssetDynamicSchema.Types.Any(type => AssetDynamicSchema.Slot(type) == field);
    internal static IQueryable<AssetFieldValueRow> Apply(AssetsDbContext db, ResourceQuery query)
    {
        IQueryable<AssetFieldValueRow> source = db.FieldValues.AsNoTracking().Include(row => row.Definition);
        foreach (var filter in query.Filters.Where(filter => IsSlot(filter.Field)))
        {
            if (filter.Value is TextValue text) AssetsPersistence.QueryText(AssetResources.FieldValue, filter.Field, text.Value);
            var type = AssetDynamicSchema.Types.Single(type => AssetDynamicSchema.Slot(type) == filter.Field);
            // Select only this canonical slot's definition type, including for NE.
            // The existing definition navigation is not Asset relation traversal.
            source = source.Where(row => row.Definition.FieldType == type);
            string left = AssetsStorage.ValueJsonColumn;
            string right = "CAST({0} AS jsonb)";
            if (type is ScalarType.String or ScalarType.Date)
            {
                // Canonical DATE is fixed-width ISO yyyy-MM-dd: ordinal text order
                // equals date order. No timezone/session DateStyle interpretation.
                left = $"jsonb_extract_path_text({left}, VARIADIC ARRAY[]::text[]) COLLATE \"{AssetsStorage.OrdinalCollation}\"";
                right = $"jsonb_extract_path_text({right}, VARIADIC ARRAY[]::text[]) COLLATE \"{AssetsStorage.OrdinalCollation}\"";
            }
            string predicate = filter.Operator switch
            {
                ResourceFilterOperator.Contains => $"strpos({left}, {right}) > 0",
                ResourceFilterOperator.StartsWith => $"starts_with({left}, {right})",
                _ => $"{left} {Operator(filter.Operator)} {right}"
            };
            // SQL identifiers/operators above come only from module constants and
            // validated enums. Every caller value is a bound canonical JSON scalar.
            string sql = $"SELECT * FROM {AssetsStorage.FieldValues} WHERE {predicate}";
            var matching = db.FieldValues.FromSqlInterpolated(FormattableStringFactory.Create(sql,
                ResourceTransport.Project(filter.Value).GetRawText())).Select(row => row.Id);
            source = source.Where(row => matching.Contains(row.Id));
        }
        return source;
    }
    private static string Operator(ResourceFilterOperator operation) => operation switch
    {
        ResourceFilterOperator.Equal => "=",
        ResourceFilterOperator.NotEqual => "<>",
        ResourceFilterOperator.LessThan => "<",
        ResourceFilterOperator.LessThanOrEqual => "<=",
        ResourceFilterOperator.GreaterThan => ">",
        ResourceFilterOperator.GreaterThanOrEqual => ">=",
        _ => throw new InvalidOperationException("Unsupported validated dynamic filter.")
    };
}
