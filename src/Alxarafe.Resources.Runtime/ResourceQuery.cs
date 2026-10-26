namespace Alxarafe.Resources.Runtime;

public enum ResourceFilterOperator { Equal, NotEqual, LessThan, LessThanOrEqual, GreaterThan, GreaterThanOrEqual, Contains, StartsWith }
public enum ResourceSortDirection { Ascending, Descending }
public sealed record ResourceFilter(string Field, ResourceFilterOperator Operator, ScalarValue Value);
public sealed record ResourceSort(string Field, ResourceSortDirection Direction = ResourceSortDirection.Ascending);

public sealed class ResourceQuery
{
    public const int DefaultLimit = 50;
    public const int MaximumLimit = 100;
    public const int MaximumSearchLength = 512;
    public static IReadOnlyList<ResourceFilterOperator> AllowedOperators(FieldDefinition field)
    {
        if (!field.Capabilities.Filterable) return Array.Empty<ResourceFilterOperator>();
        bool text = field.Type is ScalarType.String or ScalarType.Text;
        bool ordered = text || NumericDomain.Intrinsic(field.Type) is not null || field.Type is ScalarType.Date or ScalarType.Time or ScalarType.DateTime;
        return Array.AsReadOnly(Enum.GetValues<ResourceFilterOperator>().Where(operation => operation switch
        {
            ResourceFilterOperator.Equal or ResourceFilterOperator.NotEqual => true,
            ResourceFilterOperator.Contains or ResourceFilterOperator.StartsWith => text,
            _ => ordered
        }).ToArray());
    }
    public long Offset { get; }
    public int Limit { get; }
    public IReadOnlyList<ResourceFilter> Filters { get; }
    public IReadOnlyList<ResourceSort> Sorting { get; }
    public string? Search { get; }
    public ResourceQuery(long offset = 0, int limit = DefaultLimit, IEnumerable<ResourceFilter>? filters = null,
        IEnumerable<ResourceSort>? sorting = null, string? search = null)
    {
        Offset = offset; Limit = limit; Filters = Array.AsReadOnly(filters?.ToArray() ?? []);
        Sorting = Array.AsReadOnly(sorting?.ToArray() ?? []); Search = search;
    }
}
public sealed class ResourcePage
{
    public IReadOnlyList<ResourceRecord> Items { get; }
    public long Offset { get; }
    public int Limit { get; }
    public long? Total { get; }
    public ResourcePage(IEnumerable<ResourceRecord> items, long offset, int limit, long? total = null)
    {
        Items = Array.AsReadOnly(items.ToArray()); Offset = offset; Limit = limit; Total = total;
    }
}
