using Alxarafe.Modularity;
using Alxarafe.Resources;
using Xunit;

namespace Alxarafe.Resources.Tests;

public sealed class RegistryTests
{
    [Fact]
    public void RepresentativeCustomerCountryProvidersAreCollectedAndFrozen()
    {
        var definitions = new List<ResourceDefinition> { Fixture.Customer };
        var first = new Fixture.Provider(definitions);
        var second = new Fixture.Provider([Fixture.Country]);
        var registry = new ResourceRegistry(Fixture.Modules(), [first, second]);
        Assert.Equal([Fixture.CountryId, Fixture.CustomerId], registry.All.Select(resource => resource.Id));
        Assert.True(registry.TryGet(Fixture.CustomerId, out var customer));
        Assert.Same(customer, registry.Require(Fixture.CustomerId));
        Assert.DoesNotContain(ResourceOperation.Delete, customer!.Operations);
        Assert.Equal(4, customer.Operations.Count);
        definitions.Clear();
        Assert.Equal(2, registry.All.Count);
        Assert.Equal(1, first.Calls);
        Assert.Equal(1, second.Calls);
        Assert.Throws<NotSupportedException>(() => ((IList<ResourceDefinition>)registry.All).Clear());
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)registry.DefinitionHashes).Clear());
        Assert.False(registry.TryGet("missing", out _));
        Assert.Throws<KeyNotFoundException>(() => registry.Require("missing"));
    }

    [Fact]
    public void DefinitionCollectionsCopyInputsAndExposeNoMutableStorage()
    {
        var fields = new List<FieldDefinition> { Fixture.Id };
        var annotations = new List<Annotation> { new("Common", "Label", new AnnotationText("Customer")) };
        var permissions = new List<string> { Fixture.ReadPermission };
        var security = new ResourceSecurity([new(ResourceOperation.Get, permissions)]);
        var definition = Fixture.Resource(fields, security: security, annotations: annotations);
        fields.Clear(); annotations.Clear(); permissions.Clear();
        Assert.Single(Fixture.Registry(definition).All);
        Assert.Single(definition.Fields);
        Assert.Single(definition.Annotations);
        Assert.Throws<NotSupportedException>(() => ((IList<FieldDefinition>)definition.Fields).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<string>)security.Rules[0].Permissions).Clear());
    }

    [Fact]
    public void RegisteredModuleProviderUsesTheCapturedOwnerAtConstruction()
    {
        var definition = Fixture.Resource();
        var module = new Fixture.ContributingModule(Fixture.ModuleId, [definition]);
        var registry = new ResourceRegistry(Fixture.Modules(module), [module]);
        Assert.Same(definition, Assert.Single(registry.All));
        Assert.Equal(Fixture.ModuleId, registry.Require(definition.Id).ModuleId);
        Assert.Equal(Fixture.Registry(definition).DefinitionHashes[definition.Id], registry.DefinitionHashes[definition.Id]);
    }

    [Fact]
    public void RegisteredModuleProviderCannotClaimAnotherOwnersIdentity()
    {
        var module = new Fixture.ContributingModule(Fixture.ModuleId, [Fixture.Resource(moduleId: Fixture.OtherModuleId)]);
        var other = new Fixture.Module(Fixture.OtherModuleId);
        var modules = Fixture.Modules(module, other);
        module.Descriptor = other.Descriptor;
        Assert.Equal(ResourceValidationCode.UnknownModule,
            Assert.Throws<ResourceValidationException>(() => new ResourceRegistry(modules, [module])).Code);
    }

    [Theory]
    [InlineData("duplicateResource", ResourceValidationCode.Duplicate)]
    [InlineData("duplicateField", ResourceValidationCode.Duplicate)]
    [InlineData("unknownModule", ResourceValidationCode.UnknownModule)]
    [InlineData("invalidResourceId", ResourceValidationCode.InvalidIdentity)]
    [InlineData("invalidVersion", ResourceValidationCode.InvalidIdentity)]
    [InlineData("invalidField", ResourceValidationCode.InvalidField)]
    [InlineData("requiredNullable", ResourceValidationCode.InvalidField)]
    [InlineData("requiredReadOnly", ResourceValidationCode.InvalidField)]
    [InlineData("searchableNumber", ResourceValidationCode.InvalidField)]
    [InlineData("unknownType", ResourceValidationCode.InvalidField)]
    [InlineData("missingId", ResourceValidationCode.InvalidField)]
    [InlineData("mutableId", ResourceValidationCode.InvalidField)]
    [InlineData("nullableId", ResourceValidationCode.InvalidField)]
    [InlineData("unsupportedIdType", ResourceValidationCode.InvalidField)]
    [InlineData("optionalClientId", ResourceValidationCode.InvalidField)]
    [InlineData("duplicateEnum", ResourceValidationCode.InvalidField)]
    public void InvalidLocalDefinitionsFailDuringRegistration(string fault, ResourceValidationCode expected)
    {
        ResourceDefinition[] resources = fault switch
        {
            "duplicateResource" => [Fixture.Resource(), Fixture.Resource()],
            "duplicateField" => [Fixture.Resource([Fixture.Id, Fixture.Id])],
            "unknownModule" => [Fixture.Resource(moduleId: "unknown")],
            "invalidResourceId" => [Fixture.Resource(id: "Bad ID")],
            "invalidVersion" => [Fixture.Resource(version: "latest")],
            "invalidField" => [Fixture.WithField(new("bad-name", ScalarType.String))],
            "requiredNullable" => [Fixture.WithField(new("value", ScalarType.String, nullable: true, required: true))],
            "requiredReadOnly" => [Fixture.WithField(new("value", ScalarType.String, required: true, readOnly: true))],
            "searchableNumber" => [Fixture.WithField(new("value", ScalarType.Int32, capabilities: new(Searchable: true)))],
            "unknownType" => [Fixture.WithField(new("value", (ScalarType)int.MaxValue))],
            "missingId" => [Fixture.Resource(idDefinition: new("missing"))],
            "mutableId" => [Fixture.Resource([new("id", ScalarType.Uuid, readOnly: true)])],
            "nullableId" => [Fixture.Resource([new("id", ScalarType.Uuid, nullable: true, readOnly: true, immutable: true)])],
            "unsupportedIdType" => [Fixture.Resource([new("id", ScalarType.Boolean, readOnly: true, immutable: true)])],
            "optionalClientId" => [Fixture.Resource([new("id", ScalarType.Uuid, immutable: true)])],
            "duplicateEnum" => [Fixture.WithField(new("value", ScalarType.Enum, enumValues: ["a", "a"]))],
            _ => throw new ArgumentOutOfRangeException(nameof(fault))
        };
        var error = Assert.Throws<ResourceValidationException>(() => Fixture.Registry(resources));
        Assert.Equal(expected, error.Code);
        Assert.NotEmpty(error.ResourceId);
        Assert.NotEmpty(error.Path);
    }

    [Theory]
    [InlineData("unknownResource")]
    [InlineData("unknownLocalField")]
    [InlineData("unknownTargetField")]
    [InlineData("unknownDisplayField")]
    [InlineData("incompatibleType")]
    [InlineData("nullableRequired")]
    [InlineData("invalidKind")]
    public void RelationsAreValidatedOnlyAfterAllResourcesAreCollected(string fault)
    {
        var relation = new RelationDefinition("country", fault == "invalidKind" ? (RelationKind)int.MaxValue : RelationKind.ManyToOne,
            fault == "unknownResource" ? "unknown" : Fixture.CountryId, fault == "unknownLocalField" ? "missing" : "countryId",
            fault == "unknownTargetField" ? "missing" : fault == "incompatibleType" ? "name" : "id",
            fault == "unknownDisplayField" ? "missing" : "name", Required: fault == "nullableRequired");
        var customer = Fixture.Resource([Fixture.Id, new("countryId", ScalarType.Uuid, nullable: true)], relations: [relation]);
        Assert.Equal(ResourceValidationCode.InvalidRelation,
            Assert.Throws<ResourceValidationException>(() => Fixture.Registry(customer, Fixture.Country)).Code);
    }

    [Fact]
    public void CircularBusinessRelationsAreNotModuleDependencyCycles()
    {
        var first = Fixture.Resource([Fixture.Id, new("countryId", ScalarType.Uuid)],
            relations: [new("country", RelationKind.ManyToOne, Fixture.CountryId, "countryId", "id")]);
        var second = Fixture.Resource([Fixture.Id, new("customerId", ScalarType.Uuid)], id: Fixture.CountryId,
            relations: [new("customer", RelationKind.ManyToOne, Fixture.CustomerId, "customerId", "id")]);
        Assert.Equal(2, Fixture.Registry(first, second).All.Count);
        Assert.Equal(ResourceValidationCode.Duplicate, Assert.Throws<ResourceValidationException>(() => Fixture.Registry(
            Fixture.Resource([Fixture.Id, new("countryId", ScalarType.Uuid)], relations: [first.Relations[0], first.Relations[0]]), second)).Code);
    }

    [Theory]
    [InlineData("missingPermission")]
    [InlineData("unsupportedOperation")]
    [InlineData("invalidPermission")]
    [InlineData("duplicateOperation")]
    [InlineData("invalidOperation")]
    public void OperationSecurityIsExplicitFailClosedMetadata(string fault)
    {
        var operations = fault == "duplicateOperation" ? new[] { ResourceOperation.Get, ResourceOperation.Get }
            : fault == "invalidOperation" ? [(ResourceOperation)int.MaxValue] : new[] { ResourceOperation.Get };
        var security = fault switch
        {
            "missingPermission" => new ResourceSecurity([new(ResourceOperation.Get, [])]),
            "unsupportedOperation" => new ResourceSecurity([new(ResourceOperation.Get, [Fixture.ReadPermission]), new(ResourceOperation.Delete, ["customer.delete"])]),
            "invalidPermission" => new ResourceSecurity([new(ResourceOperation.Get, ["invalid permission"])]),
            _ => new ResourceSecurity([new(ResourceOperation.Get, [Fixture.ReadPermission])])
        };
        Assert.Equal(ResourceValidationCode.InvalidSecurity,
            Assert.Throws<ResourceValidationException>(() => Fixture.Registry(Fixture.Resource(operations: operations, security: security))).Code);
    }

    [Theory]
    [InlineData(ActionScope.Entity, false)]
    [InlineData(ActionScope.Collection, false)]
    [InlineData(ActionScope.Selection, true)]
    public void ActionsDeclareTypedScopesWithoutExecution(ActionScope scope, bool selection)
    {
        Assert.Single(Fixture.Registry(Fixture.Resource(actions: [new("invoice.post", scope, "invoice.post", selection)])).All);
        Assert.Equal(ResourceValidationCode.InvalidAction, Assert.Throws<ResourceValidationException>(() =>
            Fixture.Registry(Fixture.Resource(actions: [new("invoice.post", scope, "invoice.post", !selection)]))).Code);
    }

    [Fact]
    public void ActionsRejectDuplicatesMalformedNamesScopesAndPermissions()
    {
        var action = new ActionDefinition("invoice.post", ActionScope.Entity, "invoice.post");
        Assert.Equal(ResourceValidationCode.Duplicate, Assert.Throws<ResourceValidationException>(() =>
            Fixture.Registry(Fixture.Resource(actions: [action, action]))).Code);
        foreach (var invalid in new[]
        {
            new ActionDefinition("bad name", ActionScope.Entity, "invoice.post"),
            new ActionDefinition("invoice.post", (ActionScope)int.MaxValue, "invoice.post"),
            new ActionDefinition("invoice.post", ActionScope.Entity, "")
        }) Assert.Equal(ResourceValidationCode.InvalidAction, Assert.Throws<ResourceValidationException>(() => Fixture.Registry(Fixture.Resource(actions: [invalid]))).Code);
    }

    [Fact]
    public void TypedVocabulariesAreExtensibleAndValidateKeysShapesTargetsAndReferences()
    {
        var term = new AnnotationTerm("Warehouse", "Hazardous", AnnotationValueKind.Boolean, AnnotationTarget.Resource);
        var vocabulary = new AnnotationVocabulary(term.Vocabulary, [term]);
        var resource = Fixture.Resource(annotations: [new(term.Vocabulary, term.Name, new AnnotationBoolean(true))]);
        Assert.Single(new ResourceRegistry(Fixture.Modules(), [new Fixture.Provider([resource])], [vocabulary]).All);
        Annotation[] invalid =
        [new("Unknown", "Term", new AnnotationText("value")), new("UI", "MatInput", new AnnotationBoolean(true)),
            new("Common", "Label", new AnnotationBoolean(true)), new("UI", "Hidden", new AnnotationBoolean(true)),
            new("UI", "SelectionFields", new AnnotationFieldNames(["missing"]))];
        foreach (var annotation in invalid)
            Assert.Equal(ResourceValidationCode.InvalidAnnotation, Assert.Throws<ResourceValidationException>(() =>
                Fixture.Registry(Fixture.Resource(annotations: [annotation]))).Code);
        Assert.Equal(ResourceValidationCode.InvalidAnnotation, Assert.Throws<ResourceValidationException>(() =>
            new ResourceRegistry(Fixture.Modules(), [], [new("Other", [term])])).Code);
    }
}
