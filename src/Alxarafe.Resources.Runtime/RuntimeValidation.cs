using System.Diagnostics.CodeAnalysis;

namespace Alxarafe.Resources.Runtime;

internal static class RuntimeValidation
{
    [DoesNotReturn]
    private static void Reject(string resourceId, string path, ResourceInputError code) =>
        throw new ResourceRuntimeException(resourceId, ResourceRuntimeError.ValidationFailed,
            [new(path, code, "Input does not satisfy the resource definition.")]);

    internal static void Identifier(ResourceDefinition definition, ResourceIdentifier identifier)
    {
        var field = definition.Fields.Single(field => field.Name == definition.IdDefinition.Field);
        if (identifier is null || identifier.Field != field.Name || !ResourceValueValidator.IsValid(field, identifier.Value))
            Reject(definition.Id, definition.IdDefinition.Field, ResourceInputError.InvalidValue);
    }

    internal static ResourceRecord Input(ResourceDefinition definition, ResourceRecord input, bool create)
    {
        if (input is null) Reject(definition.Id, "values", ResourceInputError.Required);
        var fields = definition.Fields.ToDictionary(field => field.Name, StringComparer.Ordinal);
        var values = new SortedDictionary<string, ScalarValue>(StringComparer.Ordinal);
        foreach (var pair in input!.Values)
        {
            if (!fields.TryGetValue(pair.Key, out var field)) Reject(definition.Id, pair.Key, ResourceInputError.UnknownField);
            if (!create && pair.Key == definition.IdDefinition.Field) Reject(definition.Id, pair.Key, ResourceInputError.IdentifierMutation);
            if (field!.ReadOnly) Reject(definition.Id, pair.Key, ResourceInputError.ReadOnly);
            if (!ResourceValueValidator.IsValid(field, pair.Value)) Reject(definition.Id, pair.Key, ResourceInputError.InvalidValue);
            if (!create && field.Immutable) throw new ResourceConflictException(ResourceConflictException.ImmutableField,
                [new(pair.Key, ResourceInputError.Immutable, "Field is immutable after creation.")]);
            values.Add(pair.Key, pair.Value);
        }
        if (create)
        {
            foreach (var field in definition.Fields)
            {
                if (!values.ContainsKey(field.Name) && field.DefaultValue is { } value)
                {
                    if (!ResourceValueValidator.IsValid(field, value)) Reject(definition.Id, field.Name, ResourceInputError.InvalidValue);
                    values.Add(field.Name, value);
                }
                if (field.Required && !values.ContainsKey(field.Name)) Reject(definition.Id, field.Name, ResourceInputError.Required);
            }
        }
        return new(values);
    }

    internal static void Complete(ResourceDefinition definition, ResourceRecord record, ResourceIdentifier? identifier = null)
    {
        if (record is null) throw new ResourceRuntimeException(definition.Id, ResourceRuntimeError.InvalidRepositoryResult);
        foreach (var pair in record.Values)
        {
            var field = definition.Fields.SingleOrDefault(field => field.Name == pair.Key);
            if (field is null || !ResourceValueValidator.IsValid(field, pair.Value))
                throw new ResourceRuntimeException(definition.Id, ResourceRuntimeError.InvalidRepositoryResult);
        }
        if (definition.Fields.Any(field => field.Required && !record.Values.ContainsKey(field.Name))
            || !record.Values.TryGetValue(definition.IdDefinition.Field, out var id)
            || (identifier is not null && id != identifier.Value))
            throw new ResourceRuntimeException(definition.Id, ResourceRuntimeError.InvalidRepositoryResult);
    }

    internal static ResourceRecord Merge(ResourceRecord current, ResourceRecord patch)
    {
        var values = current.Values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        foreach (var pair in patch.Values) values[pair.Key] = pair.Value;
        return new(values);
    }

    internal static void Query(ResourceDefinition definition, ResourceQuery query)
    {
        if (query.Offset < 0 || query.Limit < 1 || query.Limit > ResourceQuery.MaximumLimit)
            Reject(definition.Id, "pagination", ResourceInputError.InvalidQuery);
        var fields = definition.Fields.ToDictionary(field => field.Name, StringComparer.Ordinal);
        foreach (var filter in query.Filters)
        {
            var field = filter?.Field is { } name ? fields.GetValueOrDefault(name) : null;
            if (filter is null || field is null || !field.Capabilities.Filterable || !Enum.IsDefined(filter.Operator))
                Reject(definition.Id, "filters", ResourceInputError.InvalidQuery);
            bool fragment = filter!.Operator is ResourceFilterOperator.Contains or ResourceFilterOperator.StartsWith;
            if ((fragment && (filter.Value is not TextValue part || !ResourceValueValidator.IsText(part.Value)))
                || (!fragment && !ResourceValueValidator.IsValid(field!, filter.Value))
                || !ResourceQuery.AllowedOperators(field!).Contains(filter.Operator)
                || (filter.Operator is not (ResourceFilterOperator.Equal or ResourceFilterOperator.NotEqual) && filter.Value is NullValue))
                Reject(definition.Id, "filters." + filter.Field, ResourceInputError.InvalidQuery);
        }
        var sorted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sort in query.Sorting)
            if (sort is null || sort.Field is null || !fields.TryGetValue(sort.Field, out var field) || !field.Capabilities.Sortable
                || !Enum.IsDefined(sort.Direction) || !sorted.Add(sort.Field)) Reject(definition.Id, "sorting", ResourceInputError.InvalidQuery);
        if (query.Search is not null && (string.IsNullOrWhiteSpace(query.Search) || query.Search.Length > ResourceQuery.MaximumSearchLength
            || !ResourceValueValidator.IsText(query.Search) || !definition.Fields.Any(field => field.Capabilities.Searchable)))
            Reject(definition.Id, "search", ResourceInputError.InvalidQuery);
    }

    internal static void Page(ResourceDefinition definition, ResourcePage page, ResourceQuery query)
    {
        if (page is null || page.Offset != query.Offset || page.Limit != query.Limit || page.Items.Count > query.Limit || page.Total < 0)
            throw new ResourceRuntimeException(definition.Id, ResourceRuntimeError.InvalidRepositoryResult);
        if (page.Total is { } total && page.Items.Count > 0 && (query.Offset > total || page.Items.Count > total - query.Offset))
            throw new ResourceRuntimeException(definition.Id, ResourceRuntimeError.InvalidRepositoryResult);
        foreach (var record in page.Items) Complete(definition, record);
        if (page.Items.Select(record => record.Values[definition.IdDefinition.Field]).Distinct().Count() != page.Items.Count)
            throw new ResourceRuntimeException(definition.Id, ResourceRuntimeError.InvalidRepositoryResult);
    }
}
