using Alxarafe.Modularity;
using Alxarafe.Resources;
using Alxarafe.Resources.Runtime;
using Xunit;

namespace Alxarafe.Resources.Runtime.Tests;

public sealed class QueryAndRegistryTests
{
    [Theory]
    [InlineData(ScalarType.Boolean, 2)]
    [InlineData(ScalarType.Uuid, 2)]
    [InlineData(ScalarType.Enum, 2)]
    [InlineData(ScalarType.String, 8)]
    [InlineData(ScalarType.Text, 8)]
    [InlineData(ScalarType.Int32, 6)]
    [InlineData(ScalarType.Decimal, 6)]
    [InlineData(ScalarType.Date, 6)]
    [InlineData(ScalarType.DateTime, 6)]
    public void PublicOperatorCatalogMatchesExistingTypedQueryPolicy(ScalarType type, int count)
    {
        Assert.Equal(count, ResourceQuery.AllowedOperators(new("field", type, capabilities: new(Filterable: true))).Count);
        Assert.Empty(ResourceQuery.AllowedOperators(new("field", type)));
    }

    [Fact]
    public async Task OperatorMetadataDoesNotChangeExistingQueryIssuePaths()
    {
        var fixture = new Fixture();
        foreach (var operation in new[] { ResourceFilterOperator.LessThan, ResourceFilterOperator.Contains })
        {
            ScalarValue value = operation == ResourceFilterOperator.Contains ? new TextValue("true") : new BooleanValue(true);
            var failure = await Assert.ThrowsAsync<ResourceRuntimeException>(() => fixture.Runtime.ListAsync(Fixture.ResourceId,
                new(filters: [new("active", operation, value)])));
            Assert.Equal("filters.active", Assert.Single(failure.Issues).Path);
        }
        var unknown = await Assert.ThrowsAsync<ResourceRuntimeException>(() => fixture.Runtime.ListAsync(Fixture.ResourceId,
            new(filters: [new("active", (ResourceFilterOperator)int.MaxValue, new BooleanValue(true))])));
        Assert.Equal("filters", Assert.Single(unknown.Issues).Path);
        Assert.Equal(0, fixture.Repository.Calls);
    }

    [Fact]
    public async Task RegistrationCannotBindARepositoryUnderAnIdentityItNeverValidated()
    {
        var fixture = new Fixture();
        var metadata = new ResourceRegistry(fixture.Modules, [new Fixture.Provider([Fixture.Definition(), Fixture.Definition(id: Fixture.OtherId)])]);
        var catalog = new ResourceRepositoryCatalog(metadata, fixture.Modules, [new(Fixture.ResourceId, ResourceRepositoryScope.Tenant)]);
        var repository = new ChangingIdentityRepository();
        var registry = new ResourceRepositoryRegistry(metadata, catalog, [repository], []);
        var runtime = new ResourceRuntime(metadata, new ModuleStateService(fixture.Modules, fixture.States), catalog, registry, fixture.Tenant, fixture.Authorization);
        var failure = await Assert.ThrowsAsync<ResourceRuntimeException>(() => runtime.GetAsync(Fixture.OtherId, Fixture.Id));
        Assert.Equal(ResourceRuntimeError.RepositoryNotRegistered, failure.Code);
        Assert.Equal(0, repository.Inner.Calls);
    }

    private sealed class ChangingIdentityRepository : IResourceRepository
    {
        private int reads;
        public string ResourceId => reads++ == 0 ? Fixture.ResourceId : Fixture.OtherId;
        internal MemoryRepository Inner { get; } = new(Fixture.ResourceId);
        public Task<ResourcePage> ListAsync(ResourceQuery query, CancellationToken cancellationToken = default) => Inner.ListAsync(query, cancellationToken);
        public Task<ResourceRecord?> GetAsync(ResourceIdentifier identifier, CancellationToken cancellationToken = default) => Inner.GetAsync(identifier, cancellationToken);
        public Task<ResourceRecord> CreateAsync(ResourceRecord values, CancellationToken cancellationToken = default) => Inner.CreateAsync(values, cancellationToken);
        public Task<ResourceRecord?> UpdateAsync(ResourceIdentifier identifier, ResourceRecord patch, CancellationToken cancellationToken = default) => Inner.UpdateAsync(identifier, patch, cancellationToken);
        public Task<bool> DeleteAsync(ResourceIdentifier identifier, CancellationToken cancellationToken = default) => Inner.DeleteAsync(identifier, cancellationToken);
    }

    [Fact]
    public async Task WhitelistedQueryIsPassedWithoutReorderingAndHasBoundedResults()
    {
        var fixture = new Fixture();
        var query = new ResourceQuery(filters: [new("active", ResourceFilterOperator.Equal, new BooleanValue(true)),
            new("code", ResourceFilterOperator.StartsWith, new TextValue("C"))],
            sorting: [new("name", ResourceSortDirection.Descending), new("code")], search: "Customer");
        var page = await fixture.Runtime.ListAsync(Fixture.ResourceId, query);
        Assert.Single(page.Items);
        Assert.Same(query, fixture.Repository.LastQuery);
        Assert.Equal(["name", "code"], fixture.Repository.LastQuery!.Sorting.Select(sort => sort.Field));
        Assert.Equal(ResourceQuery.DefaultLimit, page.Limit);
        Assert.Equal(1, page.Total);
        var empty = await fixture.Runtime.ListAsync(Fixture.ResourceId, new(filters: [new("active", ResourceFilterOperator.Equal, new BooleanValue(false))]));
        Assert.Empty(empty.Items);
    }

    [Theory]
    [InlineData("unknownFilter")]
    [InlineData("notFilterable")]
    [InlineData("wrongFilterType")]
    [InlineData("incompatibleOperator")]
    [InlineData("invalidOperator")]
    [InlineData("unknownSort")]
    [InlineData("notSortable")]
    [InlineData("duplicateSort")]
    [InlineData("invalidDirection")]
    [InlineData("negativeOffset")]
    [InlineData("zeroLimit")]
    [InlineData("hugeLimit")]
    [InlineData("emptySearch")]
    [InlineData("hugeSearch")]
    public async Task InvalidQueriesCannotReachRepository(string fault)
    {
        var fixture = new Fixture();
        var query = fault switch
        {
            "unknownFilter" => new ResourceQuery(filters: [new("unknown", ResourceFilterOperator.Equal, new TextValue("x"))]),
            "notFilterable" => new ResourceQuery(filters: [new("fixed", ResourceFilterOperator.Equal, new TextValue("x"))]),
            "wrongFilterType" => new ResourceQuery(filters: [new("active", ResourceFilterOperator.Equal, new TextValue("true"))]),
            "incompatibleOperator" => new ResourceQuery(filters: [new("active", ResourceFilterOperator.LessThan, new BooleanValue(true))]),
            "invalidOperator" => new ResourceQuery(filters: [new("code", (ResourceFilterOperator)int.MaxValue, new TextValue("x"))]),
            "unknownSort" => new ResourceQuery(sorting: [new("unknown")]),
            "notSortable" => new ResourceQuery(sorting: [new("fixed")]),
            "duplicateSort" => new ResourceQuery(sorting: [new("name"), new("name")]),
            "invalidDirection" => new ResourceQuery(sorting: [new("name", (ResourceSortDirection)int.MaxValue)]),
            "negativeOffset" => new ResourceQuery(offset: -1),
            "zeroLimit" => new ResourceQuery(limit: 0),
            "hugeLimit" => new ResourceQuery(limit: ResourceQuery.MaximumLimit + 1),
            "emptySearch" => new ResourceQuery(search: " "),
            "hugeSearch" => new ResourceQuery(search: new string('x', ResourceQuery.MaximumSearchLength + 1)),
            _ => throw new ArgumentOutOfRangeException(nameof(fault))
        };
        Assert.Equal(ResourceRuntimeError.ValidationFailed, (await Assert.ThrowsAsync<ResourceRuntimeException>(() => fixture.Runtime.ListAsync(Fixture.ResourceId, query))).Code);
        Assert.Equal(0, fixture.Repository.Calls);
    }

    [Theory]
    [InlineData(ResourceFilterOperator.Equal)]
    [InlineData(ResourceFilterOperator.NotEqual)]
    [InlineData(ResourceFilterOperator.LessThan)]
    [InlineData(ResourceFilterOperator.LessThanOrEqual)]
    [InlineData(ResourceFilterOperator.GreaterThan)]
    [InlineData(ResourceFilterOperator.GreaterThanOrEqual)]
    [InlineData(ResourceFilterOperator.Contains)]
    [InlineData(ResourceFilterOperator.StartsWith)]
    public async Task InitialOperatorSetIsWhitelistedForCompatibleTextFields(ResourceFilterOperator operation)
    {
        var fixture = new Fixture();
        await fixture.Runtime.ListAsync(Fixture.ResourceId, new(filters: [new("code", operation, new TextValue("C-1"))]));
        Assert.Equal(1, fixture.Repository.Calls);
    }

    [Fact]
    public async Task SearchNeedsSearchableFieldsAndPaginationIsBounded()
    {
        var fixture = new Fixture(searchable: false);
        Assert.Equal(ResourceRuntimeError.ValidationFailed, (await Assert.ThrowsAsync<ResourceRuntimeException>(() => fixture.Runtime.ListAsync(Fixture.ResourceId, new(search: "name")))).Code);
        Assert.Equal(0, fixture.Repository.Calls);
        await fixture.Runtime.ListAsync(Fixture.ResourceId, new(limit: ResourceQuery.MaximumLimit));
        Assert.Equal(ResourceQuery.MaximumLimit, fixture.Repository.LastQuery!.Limit);
    }

    [Fact]
    public void BindingCatalogAndScopedRepositoriesRejectAmbiguity()
    {
        var fixture = new Fixture();
        var binding = new ResourceRepositoryBinding(Fixture.ResourceId, ResourceRepositoryScope.Tenant);
        Assert.Throws<InvalidOperationException>(() => new ResourceRepositoryCatalog(fixture.Metadata, fixture.Modules, [binding, binding]));
        Assert.Throws<InvalidOperationException>(() => new ResourceRepositoryCatalog(fixture.Metadata, fixture.Modules, [new("unknown", ResourceRepositoryScope.Tenant)]));
        Assert.Throws<InvalidOperationException>(() => new ResourceRepositoryCatalog(fixture.Metadata, fixture.Modules, [new(Fixture.ResourceId, ResourceRepositoryScope.Platform)]));
        Assert.Throws<InvalidOperationException>(() => new ResourceRepositoryCatalog(fixture.Metadata, fixture.Modules, [new(Fixture.ResourceId, (ResourceRepositoryScope)int.MaxValue)]));
        Assert.Throws<InvalidOperationException>(() => new ResourceRepositoryRegistry(fixture.Metadata, fixture.Catalog, [fixture.Repository, fixture.Repository], []));
        Assert.Throws<InvalidOperationException>(() => new ResourceRepositoryRegistry(fixture.Metadata, fixture.Catalog, [new MemoryRepository(Fixture.OtherId)], []));
        Assert.Throws<NotSupportedException>(() => ((IList<ResourceRepositoryBinding>)fixture.Catalog.Bindings).Clear());
    }

    [Fact]
    public async Task RepositoryCannotChangeIdentityAfterRegistration()
    {
        var fixture = new Fixture(); fixture.Repository.ResourceId = Fixture.OtherId;
        Assert.Equal(ResourceRuntimeError.RepositoryNotRegistered, (await Assert.ThrowsAsync<ResourceRuntimeException>(() => fixture.Runtime.GetAsync(Fixture.ResourceId, Fixture.Id))).Code);
        Assert.Equal(0, fixture.Repository.Calls);
    }

    [Theory]
    [InlineData(ResourceRepositoryScope.Platform)]
    [InlineData(ResourceRepositoryScope.Tenant)]
    public async Task BothModulesUseOnlyExplicitBindingScope(ResourceRepositoryScope scope)
    {
        var fixture = new Fixture(persistence: ModulePersistence.Both, scope: scope);
        await fixture.Runtime.GetAsync(Fixture.ResourceId, Fixture.Id);
        Assert.Equal(scope == ResourceRepositoryScope.Tenant ? 1 : 0, fixture.Tenant.Calls);
    }

    [Fact]
    public async Task RegisteredSecondResourceRepositoryIsNeverUsedAsFallback()
    {
        var fixture = new Fixture();
        var metadata = new ResourceRegistry(fixture.Modules, [new Fixture.Provider([Fixture.Definition(), Fixture.Definition(id: Fixture.OtherId)])]);
        var catalog = new ResourceRepositoryCatalog(metadata, fixture.Modules,
            [new(Fixture.ResourceId, ResourceRepositoryScope.Tenant), new(Fixture.OtherId, ResourceRepositoryScope.Tenant)]);
        var other = new MemoryRepository(Fixture.OtherId);
        var repositories = new ResourceRepositoryRegistry(metadata, catalog, [other], []);
        var runtime = new ResourceRuntime(metadata, new ModuleStateService(fixture.Modules, fixture.States), catalog, repositories, fixture.Tenant, fixture.Authorization);
        Assert.Equal(ResourceRuntimeError.RepositoryNotRegistered, (await Assert.ThrowsAsync<ResourceRuntimeException>(() => runtime.GetAsync(Fixture.ResourceId, Fixture.Id))).Code);
        Assert.Equal(0, other.Calls);
        await runtime.GetAsync(Fixture.OtherId, Fixture.Id);
        Assert.Equal(1, other.Calls);
    }

    [Fact]
    public async Task UnboundBothResourceCannotChooseAnImplicitScope()
    {
        var fixture = new Fixture(persistence: ModulePersistence.Both);
        var catalog = new ResourceRepositoryCatalog(fixture.Metadata, fixture.Modules, []);
        var repositories = new ResourceRepositoryRegistry(fixture.Metadata, catalog, [], []);
        var runtime = new ResourceRuntime(fixture.Metadata, new ModuleStateService(fixture.Modules, fixture.States), catalog, repositories, fixture.Tenant, fixture.Authorization);
        Assert.Equal(ResourceRuntimeError.RepositoryNotRegistered, (await Assert.ThrowsAsync<ResourceRuntimeException>(() => runtime.GetAsync(Fixture.ResourceId, Fixture.Id))).Code);
        Assert.Equal(0, fixture.Tenant.Calls);
        Assert.Equal(0, fixture.Repository.Calls);
    }

    [Fact]
    public void RecordAndQueryCollectionsAreImmutableOrdinalSnapshots()
    {
        var values = new Dictionary<string, ScalarValue>(StringComparer.Ordinal) { ["b"] = new BooleanValue(true), ["A"] = new BooleanValue(false) };
        var record = new ResourceRecord(values); values.Clear();
        Assert.Equal(["A", "b"], record.Values.Keys);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, ScalarValue>)record.Values).Clear());
        var sorting = new List<ResourceSort> { new("name") };
        var query = new ResourceQuery(sorting: sorting); sorting.Clear();
        Assert.Single(query.Sorting);
        Assert.Throws<NotSupportedException>(() => ((IList<ResourceSort>)query.Sorting).Clear());
    }
}
