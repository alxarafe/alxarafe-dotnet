using System.Globalization;
using Alxarafe.Resources;
using Xunit;

namespace Alxarafe.Resources.Tests;

public sealed class ActionParameterTests
{
    private const string Permission = "invoice.post";
    private static ResourceDefinition Definition(params ActionParameterDefinition[] parameters) => Fixture.Resource(actions: [new(Permission, ActionScope.Entity, Permission, parameters: parameters)]);
    [Fact]
    public void ParametersAreOptionalAndSnapshotsAreImmutable()
    {
        Assert.Empty(Fixture.Registry(Definition()).Require(Fixture.CustomerId).Actions[0].Parameters);
        var values = new List<ActionParameterDefinition> { new("reason", ScalarType.String, required: true, constraints: new TextConstraints(MaximumLength: 250), annotations: [new("Common", "Label", new AnnotationText("Reason"))]) };
        var action = new ActionDefinition(Permission, ActionScope.Entity, Permission, parameters: values); values.Clear();
        var definition = Fixture.Resource(actions: [action]);
        Assert.Single(Fixture.Registry(definition).Require(Fixture.CustomerId).Actions[0].Parameters);
        Assert.Throws<NotSupportedException>(() => ((IList<ActionParameterDefinition>)action.Parameters).Clear());
    }
    [Fact]
    public void EveryScalarTypeUsesTheExistingCatalog()
    {
        foreach (var type in Enum.GetValues<ScalarType>())
            Fixture.Registry(Definition(new ActionParameterDefinition("value", type, enumValues: type == ScalarType.Enum ? ["open", "closed"] : [])));
    }
    [Theory]
    [InlineData("duplicate")]
    [InlineData("name")]
    [InlineData("nullable")]
    [InlineData("type")]
    [InlineData("constraints")]
    [InlineData("default")]
    [InlineData("enum")]
    [InlineData("annotation")]
    public void InvalidParametersFailAtRegistration(string kind)
    {
        var parameter = kind switch
        {
            "name" => new ActionParameterDefinition("bad name", ScalarType.String),
            "nullable" => new("reason", ScalarType.String, nullable: true, required: true),
            "type" => new("reason", (ScalarType)999),
            "constraints" => new("reason", ScalarType.UInt8, constraints: new NumericConstraints(Maximum: ResourceNumber.Parse("300"))),
            "default" => new("reason", ScalarType.UInt8, defaultValue: new NumericValue(ResourceNumber.Parse("300"))),
            "enum" => new("reason", ScalarType.Enum, enumValues: ["x", "x"]),
            "annotation" => new("reason", ScalarType.String, annotations: [new("Common", "Label", new AnnotationBoolean(true))]),
            _ => new("reason", ScalarType.String)
        };
        Assert.Throws<ResourceValidationException>(() => Fixture.Registry(kind == "duplicate" ? Definition(parameter, parameter) : Definition(parameter)));
    }
    [Theory]
    [InlineData("name")]
    [InlineData("type")]
    [InlineData("required")]
    [InlineData("nullable")]
    [InlineData("default")]
    [InlineData("constraint")]
    [InlineData("enum")]
    [InlineData("annotation")]
    [InlineData("remove")]
    public void EveryParameterSemanticChangeAffectsHash(string change)
    {
        var original = new ActionParameterDefinition("reason", ScalarType.String);
        var altered = change switch
        {
            "name" => new ActionParameterDefinition("other", ScalarType.String),
            "type" => new("reason", ScalarType.Text),
            "required" => new("reason", ScalarType.String, required: true),
            "nullable" => new("reason", ScalarType.String, nullable: true),
            "default" => new("reason", ScalarType.String, defaultValue: new TextValue("default")),
            "constraint" => new("reason", ScalarType.String, constraints: new TextConstraints(MaximumLength: 250)),
            "enum" => new("reason", ScalarType.Enum, enumValues: ["open", "closed"]),
            "annotation" => new("reason", ScalarType.String, annotations: [new("Common", "Label", new AnnotationText("Reason"))]),
            _ => original
        };
        string Hash(ResourceDefinition resource) => Fixture.Registry(resource).DefinitionHashes[Fixture.CustomerId];
        Assert.NotEqual(Hash(Definition(original)), Hash(change == "remove" ? Definition() : Definition(altered)));
    }
    [Fact]
    public void ParameterOrderAndCultureDoNotAffectHash()
    {
        ActionParameterDefinition[] parameters = [new("reason", ScalarType.String), new("count", ScalarType.Decimal, defaultValue: new NumericValue(ResourceNumber.Parse("1.0")))];
        string expected = Fixture.Registry(Definition(parameters)).DefinitionHashes[Fixture.CustomerId];
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            Assert.Equal(expected, Fixture.Registry(Definition(parameters.Reverse().ToArray())).DefinitionHashes[Fixture.CustomerId]);
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }
}
