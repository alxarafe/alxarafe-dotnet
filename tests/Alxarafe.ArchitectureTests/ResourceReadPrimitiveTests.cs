using System.Text.Json;
using Alxarafe.Modularity;
using Alxarafe.Modules.Ai.Application;
using Alxarafe.Modules.Assets;
using Alxarafe.Modules.Wms;
using Alxarafe.Resources;
using Alxarafe.Resources.AspNetCore;
using Alxarafe.Resources.Runtime;
using Xunit;

namespace Alxarafe.ArchitectureTests;

public sealed class ResourceReadPrimitiveTests
{
    private const string ModuleId = "fixture.resources", ResourceId = "fixture.record";
    private static readonly string[] Equality = ["eq", "ne"];
    private static readonly string[] Ordered = ["eq", "ne", "lt", "lte", "gt", "gte"];
    private static readonly string[] Fragments = ["contains", "startsWith"];
    private static readonly string[] EnumChoices = ["A", "B"];
    private static ResourceDefinition Definition(params FieldDefinition[] fields) => new(ResourceId, "1.0.0", ModuleId, new("id"),
        [new("id", ScalarType.Uuid, readOnly: true, immutable: true), .. fields], [ResourceOperation.List], new([new(ResourceOperation.List, ["fixture.read"])]));
    private static JsonElement[] Filters(ReadPrimitiveDescriptor descriptor, string field) => descriptor.InputSchema.GetProperty("properties")
        .GetProperty(ResourceReadPrimitives.Filters).GetProperty("items").GetProperty("anyOf").EnumerateArray()
        .Where(option => option.GetProperty("properties").GetProperty("field").GetProperty("enum")[0].GetString() == field).ToArray();
    private static string[] Operators(JsonElement branch) => branch.GetProperty("properties").GetProperty("operator").GetProperty("enum").EnumerateArray().Select(value => value.GetString()!).ToArray();
    private static JsonElement Value(JsonElement branch) => branch.GetProperty("properties").GetProperty("value");

    [Fact]
    public void AssetsExplicitlyOffersNineReadListsWithDefinitionPermissionsAndUniqueStableNames()
    {
        var module = new AssetsModule(); var definitions = new AssetsDefinitions().GetResourceDefinitions();
        var primitives = module.GetReadPrimitives().ToArray();
        Assert.Equal(9, primitives.Length); Assert.Equal(9, primitives.Select(row => row.Id).Distinct().Count());
        Assert.Equal(9, primitives.Select(row => row.Name).Distinct().Count());
        Assert.Equal(AssetResources.All.Order(), primitives.Select(row => row.Id[..^".list".Length]).Order());
        foreach (var primitive in primitives)
        {
            var definition = definitions.Single(row => ResourceReadPrimitives.Id(row.Id) == primitive.Id);
            Assert.True(CapabilityRegistry.IsValidId(primitive.Id));
            Assert.Equal(definition.ModuleId, primitive.Module);
            Assert.Equal(definition.Security.Rules.Single(rule => rule.Operation == ResourceOperation.List).Permissions, primitive.RequiredPermissions);
            Assert.Equal(ResourceReadPrimitives.Name(definition.Id), primitive.Name);
            AssertClosed(primitive.InputSchema);
        }
        Assert.Equal(5, WmsReadPrimitives.All.Count);
        Assert.DoesNotContain(primitives, primitive => WmsReadPrimitives.All.Any(wms => wms.Name == primitive.Name || wms.Id == primitive.Id));
    }
    private static void AssertClosed(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            if (node.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "object")
                Assert.False(node.GetProperty("additionalProperties").GetBoolean());
            foreach (var property in node.EnumerateObject()) AssertClosed(property.Value);
        }
        else if (node.ValueKind == JsonValueKind.Array) foreach (var item in node.EnumerateArray()) AssertClosed(item);
    }

    [Fact]
    public void OperatorsNullAndFragmentConstraintsFollowResourceQueryRatherThanBusinessFieldNames()
    {
        var primitive = ResourceReadPrimitives.List(Definition(
            new("text", ScalarType.String, nullable: true, constraints: new TextConstraints(3, 20), capabilities: new(Filterable: true)),
            new("number", ScalarType.Int32, capabilities: new(Filterable: true)),
            new("flag", ScalarType.Boolean, capabilities: new(Filterable: true)),
            new("reference", ScalarType.Uuid, nullable: true, capabilities: new(Filterable: true)),
            new("private", ScalarType.String)));
        var text = Filters(primitive, "text");
        Assert.Equal(Ordered, Operators(text[0])); Assert.Equal(3, Value(text[0]).GetProperty("minLength").GetInt32());
        Assert.Equal(Fragments, Operators(text[1])); Assert.False(Value(text[1]).TryGetProperty("minLength", out _));
        Assert.Equal(Equality, Operators(text[2])); Assert.Equal("null", Value(text[2]).GetProperty("type").GetString());
        Assert.Equal(Ordered, Operators(Assert.Single(Filters(primitive, "number"))));
        Assert.Equal(Equality, Operators(Assert.Single(Filters(primitive, "flag"))));
        var reference = Filters(primitive, "reference");
        Assert.Equal("uuid", Value(reference[0]).GetProperty("format").GetString()); Assert.Equal(Equality, Operators(reference[1]));
        Assert.Empty(Filters(primitive, "private"));
    }

    [Theory]
    [InlineData(ScalarType.String, "string")]
    [InlineData(ScalarType.Text, "string")]
    [InlineData(ScalarType.Boolean, "boolean")]
    [InlineData(ScalarType.Int8, "integer")]
    [InlineData(ScalarType.UInt8, "integer")]
    [InlineData(ScalarType.Int16, "integer")]
    [InlineData(ScalarType.UInt16, "integer")]
    [InlineData(ScalarType.Int32, "integer")]
    [InlineData(ScalarType.UInt32, "integer")]
    [InlineData(ScalarType.Int64, "integer")]
    [InlineData(ScalarType.Decimal, "number")]
    [InlineData(ScalarType.Uuid, "string")]
    [InlineData(ScalarType.Date, "string")]
    [InlineData(ScalarType.Time, "string")]
    [InlineData(ScalarType.DateTime, "string")]
    [InlineData(ScalarType.Enum, "string")]
    [InlineData(ScalarType.Binary, "string")]
    public void ScalarSchemasReuseCanonicalProjection(ScalarType scalar, string expected)
    {
        var primitive = ResourceReadPrimitives.List(Definition(new FieldDefinition("typed", scalar, capabilities: new(Filterable: true), enumValues: scalar == ScalarType.Enum ? EnumChoices : [])));
        Assert.Equal(expected, Value(Filters(primitive, "typed")[0]).GetProperty("type").GetString());
    }

    [Fact]
    public void PaginationAndSearchAreBoundedAndNoWriteOrArbitraryEndpointArgumentsExist()
    {
        var primitive = ResourceReadPrimitives.List(Definition(new FieldDefinition("name", ScalarType.String, capabilities: new(Searchable: true))));
        var properties = primitive.InputSchema.GetProperty("properties");
        Assert.Equal(0, properties.GetProperty("offset").GetProperty("minimum").GetInt32());
        Assert.Equal(ResourceQuery.MaximumLimit, properties.GetProperty("limit").GetProperty("maximum").GetInt32());
        Assert.Equal(ResourceQuery.DefaultLimit, properties.GetProperty("limit").GetProperty("default").GetInt32());
        Assert.Equal(ResourceQuery.MaximumSearchLength, properties.GetProperty("search").GetProperty("maxLength").GetInt32());
        Assert.Equal(3, properties.EnumerateObject().Count());
        Assert.False(ResourceReadPrimitives.List(Definition()).InputSchema.GetProperty("properties").TryGetProperty("search", out _));
        var noList = new ResourceDefinition(ResourceId, "1.0.0", ModuleId, new("id"), [new("id", ScalarType.Uuid, readOnly: true, immutable: true)], [ResourceOperation.Create], new([new(ResourceOperation.Create, ["fixture.write"])]));
        Assert.Throws<InvalidOperationException>(() => ResourceReadPrimitives.List(noList));
    }

    [Fact]
    public void RegisteredResourcesWithoutExplicitOptInCannotExecuteAsPrimitives()
    {
        var module = new Fixture(); var modules = new ModuleRegistry([new Source(module)]);
        var primitives = new ReadPrimitiveRegistry(modules, new CapabilityRegistry(modules));
        var resources = new ResourceRegistry(modules, [module]);
        Assert.Equal(2, resources.All.Count); Assert.Single(primitives.Primitives);
        var executor = new ResourceReadPrimitiveExecutor(resources, primitives, null!);
        Assert.True(executor.CanExecute(ResourceReadPrimitives.Id(ResourceId)));
        Assert.False(executor.CanExecute(ResourceReadPrimitives.Id("fixture.unexposed")));
        Assert.False(executor.CanExecute(ResourceId + ".create"));
    }
    [Fact]
    public void GenericInfrastructureAndAiHaveNoBusinessOrEfDependencies()
    {
        foreach (var assembly in new[] { typeof(AiAssistant).Assembly, typeof(ResourceReadPrimitiveExecutor).Assembly })
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name!.Contains("Assets", StringComparison.Ordinal)
                || reference.Name.Contains("EntityFramework", StringComparison.Ordinal) || reference.Name.Contains("Npgsql", StringComparison.Ordinal));
        foreach (string name in new[] { "ResourceReadPrimitives.cs", "ResourceReadPrimitiveExecutor.cs" })
        {
            string source = File.ReadAllText(Path.Combine(HostBoundaryTests.RepositoryRoot, "src/Alxarafe.Resources.AspNetCore", name));
            Assert.DoesNotContain("Asset", source, StringComparison.Ordinal); Assert.DoesNotContain("IResourceRepository", source, StringComparison.Ordinal);
        }
    }
    private sealed class Source(IAlxarafeModule module) : IModuleSource { public IEnumerable<IAlxarafeModule> GetModules() => [module]; }
    private sealed class Fixture : IAlxarafeModule, IReadPrimitiveProvider, IResourceDefinitionProvider
    {
        public ModuleDescriptor Descriptor { get; } = new(ModuleId, "1.0.0", ModuleApi.CurrentVersion, []);
        public IReadOnlyCollection<ResourceDefinition> GetResourceDefinitions() => [Definition(), new("fixture.unexposed", "1.0.0", ModuleId, new("id"), [new("id", ScalarType.Uuid, readOnly: true, immutable: true)], [ResourceOperation.List], new([new(ResourceOperation.List, ["fixture.read"])]))];
        public IEnumerable<ReadPrimitiveDescriptor> GetReadPrimitives() => [ResourceReadPrimitives.List(Definition())];
    }
}
