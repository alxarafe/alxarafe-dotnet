using Alxarafe.Core;
using Alxarafe.Modularity;
using Alxarafe.Resources;
using Alxarafe.Resources.Runtime;
using Xunit;

namespace Alxarafe.Resources.Runtime.Tests;

public sealed class ResourceRuntimeTests
{
    [Theory]
    [InlineData("-1", false)]
    [InlineData("0", true)]
    [InlineData("255", true)]
    [InlineData("256", false)]
    public async Task UInt8CreateAndPatchUseTheSameIntrinsicBounds(string text, bool valid)
    {
        var fixture = new Fixture();
        var value = new NumericValue(ResourceNumber.Parse(text));
        var create = new ResourceRecord(Fixture.Input.Values.Append(new("byteValue", value)));
        var patch = Fixture.Record(("byteValue", value));
        if (valid)
        {
            Assert.Equal(value, (await fixture.Runtime.CreateAsync(Fixture.ResourceId, create)).Values["byteValue"]);
            Assert.Equal(value, (await fixture.Runtime.UpdateAsync(Fixture.ResourceId, Fixture.Id, patch)).Values["byteValue"]);
            Assert.Equal(2, fixture.Repository.Mutations);
        }
        else
        {
            Assert.Equal(ResourceRuntimeError.ValidationFailed, (await Assert.ThrowsAsync<ResourceRuntimeException>(() => fixture.Runtime.CreateAsync(Fixture.ResourceId, create))).Code);
            Assert.Equal(ResourceRuntimeError.ValidationFailed, (await Assert.ThrowsAsync<ResourceRuntimeException>(() => fixture.Runtime.UpdateAsync(Fixture.ResourceId, Fixture.Id, patch))).Code);
            Assert.Equal(0, fixture.Repository.Calls);
        }
    }

    [Fact]
    public async Task CreateAppliesDefaultsOnlyWhenAbsentAndAllowsImmutableCreation()
    {
        var fixture = new Fixture();
        var input = new ResourceRecord(Fixture.Input.Values.Concat(Fixture.Record(("fixed", new TextValue("initial")), ("optional", new NullValue())).Values));
        var created = await fixture.Runtime.CreateAsync(Fixture.ResourceId, input);
        Assert.Equal(new BooleanValue(true), created.Values["active"]);
        Assert.IsType<NullValue>(created.Values["optional"]);
        Assert.Equal(new TextValue("initial"), created.Values["fixed"]);
        Assert.Equal(Fixture.Id.Value, created.Values["id"]);
        Assert.Equal(1, fixture.Repository.Mutations);
        Assert.False(input.Values.ContainsKey("active"));
        var supplied = new ResourceRecord(Fixture.Input.Values.Concat(Fixture.Record(("active", new BooleanValue(false))).Values));
        Assert.Equal(new BooleanValue(false), (await fixture.Runtime.CreateAsync(Fixture.ResourceId, supplied)).Values["active"]);
    }

    [Theory]
    [InlineData("missingRequired")]
    [InlineData("wrongType")]
    [InlineData("intrinsic")]
    [InlineData("constraint")]
    [InlineData("unknown")]
    [InlineData("readOnly")]
    [InlineData("notNullable")]
    [InlineData("enum")]
    public async Task InvalidCreateNeverInvokesRepository(string fault)
    {
        var fixture = new Fixture();
        var values = Fixture.Input.Values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        switch (fault)
        {
            case "missingRequired": values.Remove("code"); break;
            case "wrongType": values["code"] = new BooleanValue(true); break;
            case "intrinsic": values["creditRating"] = new NumericValue(ResourceNumber.Parse("256")); break;
            case "constraint": values["creditRating"] = new NumericValue(ResourceNumber.Parse("11")); break;
            case "unknown": values["unknown"] = new TextValue("unknown"); break;
            case "readOnly": values["id"] = Fixture.Id.Value; break;
            case "notNullable": values["active"] = new NullValue(); break;
            case "enum": values["state"] = new TextValue("unknown"); break;
        }
        var failure = await Assert.ThrowsAsync<ResourceRuntimeException>(() => fixture.Runtime.CreateAsync(Fixture.ResourceId, new(values)));
        Assert.Equal(ResourceRuntimeError.ValidationFailed, failure.Code);
        Assert.NotEmpty(failure.Issues);
        Assert.Equal(0, fixture.Repository.Calls);
    }

    [Fact]
    public async Task PartialUpdatePreservesOmittedValuesAndDoesNotApplyCreateDefaults()
    {
        var fixture = new Fixture();
        var original = new ResourceRecord(Fixture.Persisted.Values.Select(pair => pair.Key == "active" ? new KeyValuePair<string, ScalarValue>(pair.Key, new BooleanValue(false)) : pair));
        fixture.Repository.Stored = original;
        var updated = await fixture.Runtime.UpdateAsync(Fixture.ResourceId, Fixture.Id, Fixture.Record(("name", new TextValue("Updated"))));
        Assert.Equal(new TextValue("Updated"), updated.Values["name"]);
        Assert.Equal(original.Values["code"], updated.Values["code"]);
        Assert.Equal(new BooleanValue(false), updated.Values["active"]);
        Assert.Equal("name", Assert.Single(fixture.Repository.LastValues!.Values).Key);
        Assert.Equal(1, fixture.Repository.Mutations);
        Assert.False(updated.Values.ContainsKey("optional"));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("fixed")]
    [InlineData("computed")]
    [InlineData("unknown")]
    [InlineData("creditRating")]
    [InlineData("active")]
    public async Task InvalidPatchFailsBeforeEvenReadingExistingRecord(string field)
    {
        var fixture = new Fixture();
        ScalarValue value = field switch
        {
            "id" => Fixture.Id.Value,
            "creditRating" => new NumericValue(ResourceNumber.Parse("11")),
            "active" => new NullValue(),
            _ => new TextValue("changed")
        };
        var failure = await Assert.ThrowsAsync<ResourceRuntimeException>(() => fixture.Runtime.UpdateAsync(Fixture.ResourceId, Fixture.Id, Fixture.Record((field, value))));
        Assert.Equal(field == "fixed" ? ResourceRuntimeError.Conflict : ResourceRuntimeError.ValidationFailed, failure.Code);
        Assert.Equal(0, fixture.Repository.Calls);
    }

    [Theory]
    [InlineData(ResourceOperation.Create)]
    [InlineData(ResourceOperation.Update)]
    [InlineData(ResourceOperation.Delete)]
    [InlineData(ResourceOperation.Get)]
    [InlineData(ResourceOperation.List)]
    public async Task AuthoritativePermissionFailureBlocksEveryRepositoryOperation(ResourceOperation operation)
    {
        var fixture = new Fixture(delete: true);
        fixture.Authorization.Permissions.Clear();
        var error = await Assert.ThrowsAsync<ResourceRuntimeException>(() => Invoke(fixture, operation));
        Assert.Equal(ResourceRuntimeError.Forbidden, error.Code);
        Assert.Equal(0, fixture.Repository.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingTenantAndDisabledModuleAreDeniedBeforeAuthorizationAndPersistence(bool disabled)
    {
        var fixture = new Fixture();
        fixture.Tenant.Available = disabled;
        fixture.States.Enabled = !disabled;
        var error = await Assert.ThrowsAsync<ResourceRuntimeException>(() => fixture.Runtime.CreateAsync(Fixture.ResourceId, Fixture.Input));
        Assert.Equal(disabled ? ResourceRuntimeError.ModuleDisabled : ResourceRuntimeError.TenantRequired, error.Code);
        Assert.Equal(0, fixture.Authorization.Calls);
        Assert.Equal(0, fixture.Repository.Calls);
        Assert.Single(fixture.Modules.Modules);
        Assert.Single(fixture.Metadata.All);
    }

    [Fact]
    public async Task PlatformExecutionDoesNotResolveOrRequireTenant()
    {
        var fixture = new Fixture(persistence: ModulePersistence.Platform);
        fixture.Tenant.Available = false;
        await fixture.Runtime.GetAsync(Fixture.ResourceId, Fixture.Id);
        await fixture.Runtime.CreateAsync(Fixture.ResourceId, Fixture.Input);
        Assert.Equal(0, fixture.Tenant.Calls);
    }

    [Fact]
    public async Task ReadPermissionDoesNotGrantCreateOrUpdate()
    {
        var fixture = new Fixture();
        fixture.Authorization.Permissions.Clear(); fixture.Authorization.Permissions.Add(Fixture.Read);
        Assert.NotNull(await fixture.Runtime.GetAsync(Fixture.ResourceId, Fixture.Id));
        Assert.Single((await fixture.Runtime.ListAsync(Fixture.ResourceId)).Items);
        foreach (var operation in new[] { ResourceOperation.Create, ResourceOperation.Update })
            Assert.Equal(ResourceRuntimeError.Forbidden, (await Assert.ThrowsAsync<ResourceRuntimeException>(() => Invoke(fixture, operation))).Code);
        Assert.Equal(0, fixture.Repository.Mutations);
    }

    [Fact]
    public async Task TypedIdentifierAndNotFoundAreNeutralRuntimeSemantics()
    {
        var fixture = new Fixture(delete: true);
        Assert.Equal(Fixture.Persisted.Values, (await fixture.Runtime.GetAsync(Fixture.ResourceId, Fixture.Id)).Values);
        foreach (var bad in new[] { new ResourceIdentifier("id", new TextValue("opaque")), new ResourceIdentifier("name", Fixture.Id.Value) })
            Assert.Equal(ResourceRuntimeError.ValidationFailed, (await Assert.ThrowsAsync<ResourceRuntimeException>(() => fixture.Runtime.GetAsync(Fixture.ResourceId, bad))).Code);
        int calls = fixture.Repository.Calls;
        await fixture.Runtime.DeleteAsync(Fixture.ResourceId, Fixture.Id);
        Assert.Equal(ResourceRuntimeError.ResourceNotFound, (await Assert.ThrowsAsync<ResourceRuntimeException>(() => fixture.Runtime.GetAsync(Fixture.ResourceId, Fixture.Id))).Code);
        Assert.Equal(ResourceRuntimeError.ResourceNotFound, (await Assert.ThrowsAsync<ResourceRuntimeException>(() => fixture.Runtime.DeleteAsync(Fixture.ResourceId, Fixture.Id))).Code);
        Assert.True(fixture.Repository.Calls > calls);
    }

    [Fact]
    public async Task UnsupportedDeletePrecedesAllGuardsAndRepositoryAccess()
    {
        var fixture = new Fixture(); fixture.States.Enabled = false; fixture.Tenant.Available = false;
        Assert.Equal(ResourceRuntimeError.OperationNotSupported, (await Assert.ThrowsAsync<ResourceRuntimeException>(() => fixture.Runtime.DeleteAsync(Fixture.ResourceId, Fixture.Id))).Code);
        Assert.Equal(0, fixture.Tenant.Calls); Assert.Equal(0, fixture.Authorization.Calls); Assert.Equal(0, fixture.Repository.Calls);
    }

    [Fact]
    public async Task InvalidImmutableValueRemainsInputValidationRatherThanConflict()
    {
        var fixture = new Fixture();
        var failure = await Assert.ThrowsAsync<ResourceRuntimeException>(() => fixture.Runtime.UpdateAsync(Fixture.ResourceId,
            Fixture.Id, Fixture.Record(("fixed", new BooleanValue(true)))));
        Assert.Equal(ResourceRuntimeError.ValidationFailed, failure.Code);
        Assert.Equal(0, fixture.Repository.Calls);
    }

    private sealed class ConflictValidator : IResourceOperationValidator
    {
        public string ResourceId => Fixture.ResourceId;
        public string Id => "conflict";
        public Task<IReadOnlyList<ResourceIssue>> ValidateAsync(ResourceOperationContext context, CancellationToken cancellationToken = default)
            => throw new ResourceConflictException(Fixture.ConflictReason);
    }

    [Theory]
    [InlineData(ResourceOperation.Create)]
    [InlineData(ResourceOperation.Update)]
    [InlineData(ResourceOperation.Delete)]
    public async Task ExplicitRepositoryAndValidatorConflictsPreserveReasonAndState(ResourceOperation operation)
    {
        foreach (bool validator in new[] { false, true })
        {
            var fixture = new Fixture(delete: true, validators: validator ? [new ConflictValidator()] : []);
            fixture.Repository.Conflict = !validator;
            var failure = await Assert.ThrowsAsync<ResourceRuntimeException>(() => Invoke(fixture, operation));
            Assert.Equal(ResourceRuntimeError.Conflict, failure.Code);
            Assert.Equal(Fixture.ConflictReason, failure.ReasonCode);
            Assert.Equal(0, fixture.Repository.Mutations);
            Assert.Equal(Fixture.Persisted.Values, fixture.Repository.Stored!.Values);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("Not a technical code")]
    [InlineData("private SQL password=secret")]
    public void InvalidConflictReasonIsRejected(string reason)
        => Assert.Throws<ArgumentException>(() => new ResourceConflictException(reason));

    [Fact]
    public async Task BusinessValidatorsAreOrderedAndCanBlockMutations()
    {
        var calls = new List<string>();
        var a = new Fixture.Validator("a", calls); var b = new Fixture.Validator("b", calls, block: true);
        var fixture = new Fixture(delete: true, validators: [b, a]);
        foreach (var operation in new[] { ResourceOperation.Create, ResourceOperation.Update, ResourceOperation.Delete })
        {
            calls.Clear();
            Assert.Equal(ResourceRuntimeError.ValidationFailed, (await Assert.ThrowsAsync<ResourceRuntimeException>(() => Invoke(fixture, operation))).Code);
            Assert.Equal(["a", "b"], calls);
            Assert.Equal(operation, b.Context!.Operation);
            if (operation == ResourceOperation.Update)
            {
                Assert.NotNull(b.Context.Current);
                Assert.True(b.Context.Values!.Values.ContainsKey("code"));
            }
        }
        Assert.Equal(0, fixture.Repository.Mutations);
    }

    [Fact]
    public async Task InvalidRepositoryOutputAndFailuresNeverLeakAdapterData()
    {
        var fixture = new Fixture(); fixture.Repository.BadResult = true;
        Assert.Equal(ResourceRuntimeError.InvalidRepositoryResult, (await Assert.ThrowsAsync<ResourceRuntimeException>(() => fixture.Runtime.CreateAsync(Fixture.ResourceId, Fixture.Input))).Code);
        fixture.Repository.Fail = true;
        var failure = await Assert.ThrowsAsync<ResourceRuntimeException>(() => fixture.Runtime.GetAsync(Fixture.ResourceId, Fixture.Id));
        Assert.Equal(ResourceRuntimeError.ExecutionFailed, failure.Code);
        Assert.DoesNotContain("password", failure.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Null(failure.InnerException);
    }

    [Fact]
    public async Task MissingResourceRepositoryAndCancellationFailExplicitly()
    {
        var fixture = new Fixture(repository: false);
        Assert.Equal(ResourceRuntimeError.RepositoryNotRegistered, (await Assert.ThrowsAsync<ResourceRuntimeException>(() => fixture.Runtime.GetAsync(Fixture.ResourceId, Fixture.Id))).Code);
        Assert.Equal(ResourceRuntimeError.ResourceNotFound, (await Assert.ThrowsAsync<ResourceRuntimeException>(() => fixture.Runtime.ListAsync("unknown"))).Code);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Runtime.GetAsync(Fixture.ResourceId, Fixture.Id, cancelled.Token));
    }

    [Fact]
    public async Task DeniedAuthorizationCannotBeOverriddenByBusinessValidation()
    {
        var calls = new List<string>();
        var fixture = new Fixture(validators: [new Fixture.Validator("validator", calls)]);
        fixture.Authorization.Permissions.Clear();
        Assert.Equal(ResourceRuntimeError.Forbidden, (await Assert.ThrowsAsync<ResourceRuntimeException>(() => fixture.Runtime.CreateAsync(Fixture.ResourceId, Fixture.Input))).Code);
        Assert.Empty(calls);
        Assert.Equal(0, fixture.Repository.Calls);
    }

    [Fact]
    public async Task InternalUpdateReadDoesNotRequirePublicGetOrReadPermission()
    {
        var fixture = new Fixture();
        var original = fixture.Metadata.Require(Fixture.ResourceId);
        var definition = new ResourceDefinition(original.Id, original.Version, original.ModuleId, original.IdDefinition, original.Fields,
            [ResourceOperation.Update], new([new(ResourceOperation.Update, [Fixture.Update])]));
        var metadata = new ResourceRegistry(fixture.Modules, [new Fixture.Provider([definition])]);
        var catalog = new ResourceRepositoryCatalog(metadata, fixture.Modules, [new(Fixture.ResourceId, ResourceRepositoryScope.Tenant)]);
        var repositories = new ResourceRepositoryRegistry(metadata, catalog, [fixture.Repository], []);
        fixture.Authorization.Permissions.Clear(); fixture.Authorization.Permissions.Add(Fixture.Update);
        var runtime = new ResourceRuntime(metadata, new ModuleStateService(fixture.Modules, fixture.States), catalog, repositories, fixture.Tenant, fixture.Authorization);
        Assert.Equal(new TextValue("Updated"), (await runtime.UpdateAsync(Fixture.ResourceId, Fixture.Id, Fixture.Record(("name", new TextValue("Updated"))))).Values["name"]);
        Assert.Equal(ResourceRuntimeError.OperationNotSupported, (await Assert.ThrowsAsync<ResourceRuntimeException>(() => runtime.GetAsync(Fixture.ResourceId, Fixture.Id))).Code);
    }

    private static Task Invoke(Fixture fixture, ResourceOperation operation) => operation switch
    {
        ResourceOperation.Create => fixture.Runtime.CreateAsync(Fixture.ResourceId, Fixture.Input),
        ResourceOperation.Update => fixture.Runtime.UpdateAsync(Fixture.ResourceId, Fixture.Id, Fixture.Record(("name", new TextValue("Updated")))),
        ResourceOperation.Delete => fixture.Runtime.DeleteAsync(Fixture.ResourceId, Fixture.Id),
        ResourceOperation.Get => fixture.Runtime.GetAsync(Fixture.ResourceId, Fixture.Id),
        _ => fixture.Runtime.ListAsync(Fixture.ResourceId)
    };
}
