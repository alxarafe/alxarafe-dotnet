using Alxarafe.Modularity;
using Alxarafe.Resources;
using Alxarafe.Resources.Runtime;
using Xunit;

namespace Alxarafe.Resources.Runtime.Tests;

public sealed class ResourceActionTests
{
    private const string Post = "invoice.post";
    private const string Import = "item.import";
    private const string Close = "order.close";
    private const string ActionPermission = "invoice.post";
    private const string Reason = "invoice_already_posted";
    private static ResourceActionInput Input(params (string Name, ScalarValue Value)[] values) => new(values.Select(value => new KeyValuePair<string, ScalarValue>(value.Name, value.Value)));
    private static ResourceActionTarget Entity => new ResourceEntityTarget(Fixture.Id);
    private sealed class Handler(string action) : IResourceActionHandler
    {
        public string ResourceId { get; set; } = Fixture.ResourceId;
        public string ActionName { get; set; } = action;
        internal int Calls;
        internal bool Fail;
        internal ResourceActionExecutionContext? Received;
        internal ResourceActionOutcome Result = new(ResourceActionOutcomeKind.Success);
        public Task<ResourceActionOutcome> ExecuteAsync(ResourceActionExecutionContext context, CancellationToken cancellationToken = default)
        { Calls++; Received = context; if (Fail) throw new InvalidOperationException("secret SQL credential"); return Task.FromResult(Result); }
    }
    private sealed class Availability : IResourceActionAvailabilityProvider
    {
        public string ResourceId => Fixture.ResourceId;
        public string ActionName => Post;
        internal int Calls;
        internal bool Fail;
        internal ResourceActionAvailability Result = new(true);
        public Task<ResourceActionAvailability> EvaluateAsync(ResourceActionContext context, CancellationToken cancellationToken = default)
        { Calls++; if (Fail) throw new InvalidOperationException("secret SQL credential"); return Task.FromResult(Result); }
    }
    private sealed class Harness
    {
        internal Fixture Base { get; }
        internal ResourceRegistry Metadata { get; }
        internal Handler PostHandler { get; } = new(Post);
        internal Handler ImportHandler { get; } = new(Import);
        internal Handler CloseHandler { get; } = new(Close);
        internal Availability Provider { get; } = new();
        internal ResourceActionRuntime Runtime { get; }
        internal ResourceRepositoryCatalog Scopes { get; }
        internal Harness(ModulePersistence persistence = ModulePersistence.Tenant, bool handlers = true, bool provider = true)
        {
            Base = new(persistence: persistence);
            Base.Authorization.Permissions.Add(ActionPermission);
            var source = Fixture.Definition();
            Metadata = new(Base.Modules, [new Fixture.Provider([new(source.Id, source.Version, source.ModuleId, source.IdDefinition, source.Fields, source.Operations, source.Security,
                actions: [new(Post, ActionScope.Entity, ActionPermission, parameters: [new("reason", ScalarType.String, required: true, constraints: new TextConstraints(MaximumLength: 10)), new("optional", ScalarType.String, nullable: true, defaultValue: new TextValue("default")), new("count", ScalarType.UInt8, constraints: new NumericConstraints(Maximum: ResourceNumber.Parse("10"))), new("mode", ScalarType.Enum, enumValues: ["open", "closed"])]),
                    new(Import, ActionScope.Collection, Fixture.Create, parameters: [new("active", ScalarType.Boolean, defaultValue: new BooleanValue(true))]),
                    new(Close, ActionScope.Selection, Fixture.Delete, requiresSelection: true)])])]);
            Scopes = new(Metadata, Base.Modules, [new(Fixture.ResourceId, persistence == ModulePersistence.Tenant ? ResourceRepositoryScope.Tenant : ResourceRepositoryScope.Platform)]);
            Runtime = new(Metadata, new ModuleStateService(Base.Modules, Base.States), Scopes,
                new(Metadata, Scopes, handlers ? [PostHandler, ImportHandler, CloseHandler] : [], provider ? [Provider] : []), Base.Tenant, Base.Authorization);
        }
    }
    private static async Task<ResourceRuntimeException> Error(ResourceRuntimeError code, Func<Task> action)
    { var failure = await Assert.ThrowsAsync<ResourceRuntimeException>(action); Assert.Equal(code, failure.Code); Assert.DoesNotContain("secret", failure.ToString(), StringComparison.Ordinal); return failure; }
    [Fact]
    public async Task ActionAuthorityIsIndependentOfCrudAndDefaultingPreservesExplicitNull()
    {
        var h = new Harness(); h.Base.Authorization.Permissions.Clear(); h.Base.Authorization.Permissions.Add(ActionPermission);
        await h.Runtime.ExecuteAsync(Fixture.ResourceId, Post, Entity, Input(("reason", new TextValue("valid"))));
        Assert.Equal(new TextValue("default"), h.PostHandler.Received!.Input.Values["optional"]);
        await h.Runtime.ExecuteAsync(Fixture.ResourceId, Post, Entity, Input(("reason", new TextValue("valid")), ("optional", new NullValue())));
        Assert.IsType<NullValue>(h.PostHandler.Received!.Input.Values["optional"]); Assert.Equal(2, h.PostHandler.Calls); Assert.Equal(0, h.Base.Repository.Calls);
    }
    [Theory]
    [InlineData("unknown")]
    [InlineData("required")]
    [InlineData("null")]
    [InlineData("type")]
    [InlineData("constraint")]
    [InlineData("enum")]
    public async Task InvalidInputNeverReachesProviderOrHandler(string invalid)
    {
        var h = new Harness();
        var input = invalid switch
        {
            "unknown" => Input(("reason", new TextValue("ok")), ("permissions", new TextValue(Fixture.Update))),
            "required" => Input(),
            "null" => Input(("reason", new NullValue())),
            "type" => Input(("reason", new BooleanValue(true))),
            "constraint" => Input(("reason", new TextValue("ok")), ("count", new NumericValue(ResourceNumber.Parse("300")))),
            _ => Input(("reason", new TextValue("ok")), ("mode", new TextValue("invalid")))
        };
        await Error(ResourceRuntimeError.ValidationFailed, () => h.Runtime.ExecuteAsync(Fixture.ResourceId, Post, Entity, input)); Assert.Equal(0, h.Provider.Calls); Assert.Equal(0, h.PostHandler.Calls);
    }
    [Fact]
    public async Task CollectionDefaultsAndSelectionOrderNeedNoRepository()
    {
        var h = new Harness(provider: false);
        await h.Runtime.ExecuteAsync(Fixture.ResourceId, Import, new ResourceCollectionTarget(), Input());
        Assert.Equal(new BooleanValue(true), h.ImportHandler.Received!.Input.Values["active"]);
        ResourceIdentifier[] ids = [Fixture.Id, new("id", new UuidValue(Guid.NewGuid()))];
        await h.Runtime.ExecuteAsync(Fixture.ResourceId, Close, new ResourceSelectionTarget(ids), Input());
        Assert.Equal(ids, ((ResourceSelectionTarget)h.CloseHandler.Received!.Context.Target).Identifiers);
        Assert.Equal(0, h.Base.Repository.Calls);
    }
    [Theory]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("oversize")]
    [InlineData("type")]
    [InlineData("entity-shape")]
    [InlineData("collection-shape")]
    public async Task InvalidTargetsFailBeforeDomainAccess(string invalid)
    {
        var h = new Harness();
        ResourceActionTarget target = invalid switch
        {
            "empty" => new ResourceSelectionTarget([]),
            "duplicate" => new ResourceSelectionTarget([Fixture.Id, Fixture.Id]),
            "oversize" => new ResourceSelectionTarget(Enumerable.Range(0, ResourceActionRuntime.MaximumSelection + 1).Select(_ => new ResourceIdentifier("id", new UuidValue(Guid.NewGuid())))),
            "type" => new ResourceEntityTarget(new("id", new TextValue("wrong"))),
            "entity-shape" => new ResourceCollectionTarget(),
            _ => Entity
        };
        string action = invalid is "empty" or "duplicate" or "oversize" ? Close : invalid == "collection-shape" ? Import : Post;
        await Error(ResourceRuntimeError.ValidationFailed, () => h.Runtime.ExecuteAsync(Fixture.ResourceId, action, target, Input(("reason", new TextValue("ok")))));
        Assert.Equal(0, h.Provider.Calls); Assert.Equal(0, h.PostHandler.Calls); Assert.Equal(0, h.ImportHandler.Calls); Assert.Equal(0, h.CloseHandler.Calls);
    }
    [Fact]
    public async Task UnauthorizedAvailabilityIsRedactedAndRevocationBlocksExecution()
    {
        var h = new Harness(); var first = await h.Runtime.AvailabilityAsync(Fixture.ResourceId, Entity); Assert.True(Assert.Single(first).Available);
        h.Base.Authorization.Permissions.Remove(ActionPermission); h.Provider.Result = new(false, Reason);
        var state = Assert.Single(await h.Runtime.AvailabilityAsync(Fixture.ResourceId, Entity)); Assert.False(state.Authorized); Assert.False(state.Available); Assert.Null(state.UnavailableReason); Assert.Equal(1, h.Provider.Calls);
        await Error(ResourceRuntimeError.Forbidden, () => h.Runtime.ExecuteAsync(Fixture.ResourceId, Post, Entity, Input())); Assert.Equal(0, h.PostHandler.Calls); Assert.Equal(1, h.Provider.Calls);
    }
    [Fact]
    public async Task AvailabilityIsRecheckedAndUnavailableReasonIsStable()
    {
        var h = new Harness(); Assert.True(Assert.Single(await h.Runtime.AvailabilityAsync(Fixture.ResourceId, Entity)).Available);
        h.Provider.Result = new(false, Reason);
        var failure = await Error(ResourceRuntimeError.ActionUnavailable, () => h.Runtime.ExecuteAsync(Fixture.ResourceId, Post, Entity, Input(("reason", new TextValue("ok")))));
        Assert.Equal(Reason, failure.ReasonCode); Assert.Equal(2, h.Provider.Calls); Assert.Equal(0, h.PostHandler.Calls);
    }
    [Theory]
    [InlineData("provider")]
    [InlineData("handler")]
    [InlineData("malformed-reason")]
    [InlineData("malformed-outcome")]
    public async Task UnexpectedAdapterFailuresAreSanitized(string failure)
    {
        var h = new Harness(); h.Provider.Fail = failure == "provider"; h.PostHandler.Fail = failure == "handler";
        if (failure == "malformed-reason") h.Provider.Result = new(false, "secret SQL credential");
        if (failure == "malformed-outcome") h.PostHandler.Result = new(ResourceActionOutcomeKind.Conflict, "secret SQL credential");
        await Error(ResourceRuntimeError.ExecutionFailed, () => h.Runtime.ExecuteAsync(Fixture.ResourceId, Post, Entity, Input(("reason", new TextValue("ok")))));
        if (failure is "provider" or "malformed-reason") Assert.Equal(0, h.PostHandler.Calls);
    }
    [Fact]
    public async Task ExpectedOutcomesAndMissingHandlersFailExplicitly()
    {
        var h = new Harness(); h.PostHandler.Result = new(ResourceActionOutcomeKind.NotFound);
        await Error(ResourceRuntimeError.ResourceNotFound, () => h.Runtime.ExecuteAsync(Fixture.ResourceId, Post, Entity, Input(("reason", new TextValue("ok")))));
        h.PostHandler.Result = new(ResourceActionOutcomeKind.Conflict, Reason);
        Assert.Equal(Reason, (await Error(ResourceRuntimeError.ActionConflict, () => h.Runtime.ExecuteAsync(Fixture.ResourceId, Post, Entity, Input(("reason", new TextValue("ok")))))).ReasonCode);
        var missing = new Harness(handlers: false, provider: false);
        await Error(ResourceRuntimeError.ActionHandlerNotRegistered, () => missing.Runtime.ExecuteAsync(Fixture.ResourceId, Import, new ResourceCollectionTarget(), Input()));
        await Error(ResourceRuntimeError.ActionNotFound, () => h.Runtime.ExecuteAsync(Fixture.ResourceId, "unknown", Entity, Input()));
        await Error(ResourceRuntimeError.ResourceNotFound, () => h.Runtime.ExecuteAsync("unknown", Post, Entity, Input()));
    }
    [Fact]
    public async Task ModuleAndTenantGuardsBlockAllDomainAccessButPlatformRequiresNoTenant()
    {
        var h = new Harness(); h.Base.States.Enabled = false;
        await Error(ResourceRuntimeError.ModuleDisabled, () => h.Runtime.AvailabilityAsync(Fixture.ResourceId, Entity));
        await Error(ResourceRuntimeError.ModuleDisabled, () => h.Runtime.ExecuteAsync(Fixture.ResourceId, Post, Entity, Input()));
        h.Base.States.Enabled = true; h.Base.Tenant.Available = false;
        await Error(ResourceRuntimeError.TenantRequired, () => h.Runtime.ExecuteAsync(Fixture.ResourceId, Post, Entity, Input())); Assert.Equal(0, h.Provider.Calls); Assert.Equal(0, h.PostHandler.Calls);
        var platform = new Harness(ModulePersistence.Platform); platform.Base.Tenant.Available = false;
        await platform.Runtime.ExecuteAsync(Fixture.ResourceId, Import, new ResourceCollectionTarget(), Input()); Assert.Equal(0, platform.Base.Tenant.Calls);
    }
    [Fact]
    public async Task SelectionMaximumIsAllowedAndCallerOrderIsPreserved()
    {
        var h = new Harness();
        var ids = Enumerable.Range(0, ResourceActionRuntime.MaximumSelection).Select(_ => new ResourceIdentifier("id", new UuidValue(Guid.NewGuid()))).ToArray();
        await h.Runtime.ExecuteAsync(Fixture.ResourceId, Close, new ResourceSelectionTarget(ids), Input());
        Assert.Equal(ids, ((ResourceSelectionTarget)h.CloseHandler.Received!.Context.Target).Identifiers);
    }
    [Fact]
    public void CompositionRejectsUnknownDuplicateAndAmbiguousOwnership()
    {
        var h = new Harness();
        Assert.Throws<InvalidOperationException>(() => new ResourceActionRegistry(h.Metadata, h.Scopes, [h.PostHandler, h.PostHandler], []));
        Assert.Throws<InvalidOperationException>(() => new ResourceActionRegistry(h.Metadata, h.Scopes, [], [h.Provider, h.Provider]));
        h.PostHandler.ResourceId = "unknown"; Assert.Throws<InvalidOperationException>(() => new ResourceActionRegistry(h.Metadata, h.Scopes, [h.PostHandler], []));
        h.PostHandler.ResourceId = Fixture.ResourceId; h.PostHandler.ActionName = "unknown"; Assert.Throws<InvalidOperationException>(() => new ResourceActionRegistry(h.Metadata, h.Scopes, [h.PostHandler], []));
        Assert.Throws<InvalidOperationException>(() => new ResourceRepositoryCatalog(h.Metadata, h.Base.Modules, [new(Fixture.ResourceId, ResourceRepositoryScope.Platform)]));
        Assert.Throws<InvalidOperationException>(() => new ResourceRepositoryCatalog(h.Metadata, h.Base.Modules, [new(Fixture.ResourceId, (ResourceRepositoryScope)999)]));
        var both = new Harness(ModulePersistence.Both); var unbound = new ResourceRepositoryCatalog(both.Metadata, both.Base.Modules, []);
        Assert.Throws<ResourceRuntimeException>(() => new ResourceActionRegistry(both.Metadata, unbound, [both.PostHandler], []));
    }
    [Fact]
    public async Task ScopedRegistryDetectsChangedHandlerIdentity()
    {
        var h = new Harness(); h.PostHandler.ActionName = Import;
        await Error(ResourceRuntimeError.ExecutionFailed, () => h.Runtime.ExecuteAsync(Fixture.ResourceId, Post, Entity, Input(("reason", new TextValue("ok")))));
        Assert.Equal(0, h.PostHandler.Calls);
    }
}
