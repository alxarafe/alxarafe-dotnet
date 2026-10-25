using System.Collections.Concurrent;
using System.Globalization;
using Alxarafe.Core;
using Alxarafe.Modules.Wms;
using Alxarafe.Resources;
using Alxarafe.Resources.Runtime;

namespace Alxarafe.Host.IntegrationTests;

internal static class ResourceApiFixtures
{
    internal const string Customer = "fixture.api-customer";
    internal const string Country = "fixture.api-country";
    internal const string Read = "wms.inventory.read";
    internal const string Write = "wms.items.write";
    internal const string Version = "1.0.0";
    internal static readonly Guid FirstId = Guid.Parse("035a2161-c936-42dc-8b4b-d3cf5d1953a6");
    internal static ResourceRecord Record(params (string Name, ScalarValue Value)[] values) => new(values.Select(value => new KeyValuePair<string, ScalarValue>(value.Name, value.Value)));
    internal static ResourceDefinition Definition(string id = Customer, bool delete = false)
    {
        var operations = new List<ResourceOperation> { ResourceOperation.List, ResourceOperation.Get, ResourceOperation.Create, ResourceOperation.Update };
        if (delete) operations.Add(ResourceOperation.Delete);
        return new(id, Version, WmsModule.Id, new("id"),
            [new("id", ScalarType.Uuid, readOnly: true, immutable: true),
                new("code", ScalarType.String, required: true, constraints: new TextConstraints(1, 30, "^[A-Z0-9-]+$"), capabilities: new(true, true)),
                new("name", ScalarType.String, required: true, constraints: new TextConstraints(MaximumLength: 200), capabilities: new(true, true, true)),
                new("rating", ScalarType.UInt8, constraints: new NumericConstraints(ResourceNumber.Zero, ResourceNumber.Parse("10")), capabilities: new(true, true)),
                new("active", ScalarType.Boolean, defaultValue: new BooleanValue(true), capabilities: new(true)),
                new("optional", ScalarType.String, nullable: true, defaultValue: new TextValue("fallback")),
                new("fixed", ScalarType.String, immutable: true), new("state", ScalarType.Enum, enumValues: ["open", "closed"]),
                new("countryId", ScalarType.Uuid, nullable: true), new("amount", ScalarType.Decimal, constraints: new DecimalConstraints(60, 20)),
                new("signed", ScalarType.Int64), new("unsigned", ScalarType.UInt32), new("date", ScalarType.Date),
                new("time", ScalarType.Time), new("instant", ScalarType.DateTime), new("binary", ScalarType.Binary)],
            operations, new(operations.Select(operation => new OperationSecurityRule(operation, [operation is ResourceOperation.List or ResourceOperation.Get ? Read : Write]))),
            id == Customer ? [new("country", RelationKind.ManyToOne, Country, "countryId", "id", "name")] : [],
            [new("invoice.post", ActionScope.Entity, Write, annotations: [new("Common", "Label", new AnnotationText("Post"))])],
            [new("Common", "Label", new AnnotationText("Customers"))]);
    }
    internal sealed class Provider : IResourceDefinitionProvider
    {
        public IReadOnlyCollection<ResourceDefinition> GetResourceDefinitions() => [Definition(), Definition(Country, delete: true)];
    }
    internal sealed class Store
    {
        internal ConcurrentDictionary<(string Tenant, string Resource, Guid Id), ResourceRecord> Records { get; } = new();
        internal int Calls;
        internal int Mutations;
    }
    internal sealed class Repository(string resourceId, IRequestTenantContext tenant, Store store) : IResourceRepository
    {
        public string ResourceId => resourceId;
        private async Task<string> Owner(CancellationToken ct) { Interlocked.Increment(ref store.Calls); return (await tenant.RequireAsync(ct)).Id; }
        private static Guid Id(ResourceIdentifier identifier) => ((UuidValue)identifier.Value).Value;
        public async Task<ResourceRecord?> GetAsync(ResourceIdentifier identifier, CancellationToken cancellationToken = default)
        { string owner = await Owner(cancellationToken); return store.Records.GetValueOrDefault((owner, ResourceId, Id(identifier))); }
        public async Task<ResourceRecord> CreateAsync(ResourceRecord values, CancellationToken cancellationToken = default)
        {
            string owner = await Owner(cancellationToken); Interlocked.Increment(ref store.Mutations);
            var id = Guid.NewGuid(); var record = new ResourceRecord(values.Values.Append(new("id", new UuidValue(id))));
            store.Records[(owner, ResourceId, id)] = record; return record;
        }
        public async Task<ResourceRecord?> UpdateAsync(ResourceIdentifier identifier, ResourceRecord patch, CancellationToken cancellationToken = default)
        {
            string owner = await Owner(cancellationToken);
            var key = (owner, ResourceId, Id(identifier));
            if (!store.Records.TryGetValue(key, out var current)) return null;
            var values = current.Values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            foreach (var pair in patch.Values) values[pair.Key] = pair.Value;
            var record = new ResourceRecord(values); store.Records[key] = record; Interlocked.Increment(ref store.Mutations); return record;
        }
        public async Task<bool> DeleteAsync(ResourceIdentifier identifier, CancellationToken cancellationToken = default)
        { string owner = await Owner(cancellationToken); bool deleted = store.Records.TryRemove((owner, ResourceId, Id(identifier)), out _); if (deleted) Interlocked.Increment(ref store.Mutations); return deleted; }
        public async Task<ResourcePage> ListAsync(ResourceQuery query, CancellationToken cancellationToken = default)
        {
            string owner = await Owner(cancellationToken);
            IEnumerable<ResourceRecord> records = store.Records.Where(pair => pair.Key.Tenant == owner && pair.Key.Resource == ResourceId).Select(pair => pair.Value);
            foreach (var filter in query.Filters) records = records.Where(record => Match(record.Values.GetValueOrDefault(filter.Field), filter));
            if (query.Search is not null) records = records.Where(record => ((TextValue)record.Values["name"]).Value.Contains(query.Search, StringComparison.Ordinal));
            IOrderedEnumerable<ResourceRecord>? ordered = null;
            foreach (var sort in query.Sorting)
            {
                Func<ResourceRecord, string> key = record => record.Values.TryGetValue(sort.Field, out var value) ? Text(value) : "";
                ordered = ordered is null ? (sort.Direction == ResourceSortDirection.Ascending ? records.OrderBy(key, StringComparer.Ordinal) : records.OrderByDescending(key, StringComparer.Ordinal))
                    : (sort.Direction == ResourceSortDirection.Ascending ? ordered.ThenBy(key, StringComparer.Ordinal) : ordered.ThenByDescending(key, StringComparer.Ordinal));
            }
            var all = (ordered ?? records.OrderBy(record => Text(record.Values["code"]), StringComparer.Ordinal)).ToArray();
            return new(query.Offset > int.MaxValue ? [] : all.Skip((int)query.Offset).Take(query.Limit), query.Offset, query.Limit, all.LongLength);
        }
        private static string Text(ScalarValue value) => value switch { TextValue text => text.Value, NumericValue number => number.Value.CanonicalText, _ => "" };
        private static bool Match(ScalarValue? actual, ResourceFilter filter)
        {
            int comparison = actual is NumericValue a && filter.Value is NumericValue b ? a.Value.CompareTo(b.Value) : string.Compare(Text(actual ?? new NullValue()), Text(filter.Value), StringComparison.Ordinal);
            return filter.Operator switch
            {
                ResourceFilterOperator.Equal => actual == filter.Value,
                ResourceFilterOperator.NotEqual => actual != filter.Value,
                ResourceFilterOperator.LessThan => comparison < 0,
                ResourceFilterOperator.LessThanOrEqual => comparison <= 0,
                ResourceFilterOperator.GreaterThan => comparison > 0,
                ResourceFilterOperator.GreaterThanOrEqual => comparison >= 0,
                ResourceFilterOperator.Contains => Text(actual!).Contains(Text(filter.Value), StringComparison.Ordinal),
                ResourceFilterOperator.StartsWith => Text(actual!).StartsWith(Text(filter.Value), StringComparison.Ordinal),
                _ => false
            };
        }
    }
    internal sealed class DeleteValidator : IResourceOperationValidator
    {
        public string ResourceId => Country;
        public string Id => "delete-policy";
        public Task<IReadOnlyList<ResourceIssue>> ValidateAsync(ResourceOperationContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ResourceIssue>>(context.Operation == ResourceOperation.Delete && context.Identifier?.Value == new UuidValue(FirstId)
                ? [new("id", ResourceInputError.BusinessRule, "Protected fixture.")] : []);
    }
}
