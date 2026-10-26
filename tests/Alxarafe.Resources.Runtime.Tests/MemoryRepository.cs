using Alxarafe.Resources;
using Alxarafe.Resources.Runtime;

namespace Alxarafe.Resources.Runtime.Tests;

internal sealed class MemoryRepository(string resourceId) : IResourceRepository
{
    public string ResourceId { get; set; } = resourceId;
    internal int Calls { get; private set; }
    internal int Mutations { get; private set; }
    internal ResourceRecord? Stored { get; set; } = Fixture.Persisted;
    internal ResourceRecord? LastValues { get; private set; }
    internal ResourceQuery? LastQuery { get; private set; }
    internal bool Fail { get; set; }
    internal bool Conflict { get; set; }
    internal bool BadResult { get; set; }
    public Task<ResourcePage> ListAsync(ResourceQuery query, CancellationToken cancellationToken = default)
    {
        Call(); LastQuery = query;
        // Test adapter evaluates a bounded one-record fixture; production adapters own queries.
        bool matches = Stored is not null && query.Filters.All(filter => filter.Operator switch
        {
            ResourceFilterOperator.Equal => Stored.Values.GetValueOrDefault(filter.Field) == filter.Value,
            ResourceFilterOperator.NotEqual => Stored.Values.GetValueOrDefault(filter.Field) != filter.Value,
            ResourceFilterOperator.StartsWith => Stored.Values[filter.Field] is TextValue text && filter.Value is TextValue value && text.Value.StartsWith(value.Value, StringComparison.Ordinal),
            ResourceFilterOperator.Contains => Stored.Values[filter.Field] is TextValue text && filter.Value is TextValue value && text.Value.Contains(value.Value, StringComparison.Ordinal),
            _ => true
        }) && (query.Search is null || ((TextValue)Stored.Values["name"]).Value.Contains(query.Search, StringComparison.Ordinal));
        return Task.FromResult(new ResourcePage(matches && query.Offset == 0 ? [Stored!] : [], query.Offset, query.Limit, matches ? 1 : 0));
    }
    public Task<ResourceRecord?> GetAsync(ResourceIdentifier identifier, CancellationToken cancellationToken = default)
    { Call(); return Task.FromResult(Stored is not null && Stored.Values.GetValueOrDefault("id") == identifier.Value ? Stored : null); }
    public Task<ResourceRecord> CreateAsync(ResourceRecord values, CancellationToken cancellationToken = default)
    {
        Call(); Mutations++; LastValues = values;
        Stored = BadResult ? Fixture.Record(("secretColumn", new TextValue("never-return")))
            : new ResourceRecord(values.Values.Append(new("id", Fixture.Id.Value)));
        return Task.FromResult(Stored);
    }
    public Task<ResourceRecord?> UpdateAsync(ResourceIdentifier identifier, ResourceRecord patch, CancellationToken cancellationToken = default)
    {
        Call(); Mutations++; LastValues = patch;
        if (Stored is null) return Task.FromResult<ResourceRecord?>(null);
        var values = Stored.Values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        foreach (var pair in patch.Values) values[pair.Key] = pair.Value;
        Stored = new(values);
        return Task.FromResult<ResourceRecord?>(Stored);
    }
    public Task<bool> DeleteAsync(ResourceIdentifier identifier, CancellationToken cancellationToken = default)
    {
        Call(); Mutations++;
        bool exists = Stored is not null && Stored.Values["id"] == identifier.Value;
        if (exists) Stored = null;
        return Task.FromResult(exists);
    }
    private void Call()
    {
        Calls++;
        if (Conflict) throw new ResourceConflictException(Fixture.ConflictReason);
        if (Fail) throw new InvalidOperationException("SQL password=secret connection-string must not escape");
    }
}
