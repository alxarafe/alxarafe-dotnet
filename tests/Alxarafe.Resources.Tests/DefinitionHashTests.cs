using System.Globalization;
using Alxarafe.Resources;
using Xunit;

namespace Alxarafe.Resources.Tests;

public sealed class DefinitionHashTests
{
    [Fact]
    public void GoldenVectorPinsCanonicalFormatAcrossProcesses()
    {
        // SHA-256 of the documented v3 canonical projection of Fixture.Resource().
        const string expected = "987e48e21244d1e3a678a6e2ffeafb35d0c485204ab1e55ad55f6c0aedc08d2c";
        Assert.Equal(expected, Fixture.Registry(Fixture.Resource()).DefinitionHashes[Fixture.CustomerId]);
        Assert.Equal(expected, Fixture.Registry(Fixture.Resource()).DefinitionHashes[Fixture.CustomerId]);
    }

    [Fact]
    public void DeclarationProviderAndPermissionOrderCannotChangeTheHash()
    {
        var customer = Fixture.Customer;
        var reordered = new ResourceDefinition(customer.Id, customer.Version, customer.ModuleId, customer.IdDefinition,
            customer.Fields.Reverse(), customer.Operations.Reverse(),
            new ResourceSecurity(customer.Security.Rules.Reverse().Select(rule => new OperationSecurityRule(rule.Operation, rule.Permissions.Reverse()))),
            customer.Relations.Reverse(), customer.Actions.Reverse(), customer.Annotations.Reverse());
        var original = Fixture.Registry(customer, Fixture.Country);
        var reversed = Fixture.Registry(Fixture.Country, reordered);
        Assert.Equal(original.DefinitionHashes.OrderBy(pair => pair.Key, StringComparer.Ordinal), reversed.DefinitionHashes.OrderBy(pair => pair.Key, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("field")]
    [InlineData("constraint")]
    [InlineData("capability")]
    [InlineData("permission")]
    [InlineData("action")]
    [InlineData("annotation")]
    [InlineData("relation")]
    [InlineData("version")]
    public void RelevantSemanticChangesAlterTheDefinitionHash(string change)
    {
        ResourceDefinition Build(bool modified) => Fixture.Resource(
            [Fixture.Id, new("name", modified && change == "field" ? ScalarType.Text : ScalarType.String,
                constraints: new TextConstraints(MaximumLength: modified && change == "constraint" ? 31 : 30),
                capabilities: new(Filterable: !(modified && change == "capability"))), new("countryId", ScalarType.Uuid, nullable: true)],
            security: new([new(ResourceOperation.Get, [modified && change == "permission" ? "customer.other" : Fixture.ReadPermission])]),
            relations: [new("country", RelationKind.ManyToOne, Fixture.CountryId, "countryId", "id", modified && change == "relation" ? null : "name")],
            actions: [new(modified && change == "action" ? "invoice.cancel" : "invoice.post", ActionScope.Entity, "invoice.post")],
            annotations: [new("Common", "Label", new AnnotationText(modified && change == "annotation" ? "Clients" : "Customers"))],
            version: modified && change == "version" ? "1.0.1" : Fixture.Version);
        Assert.NotEqual(Fixture.Registry(Build(false), Fixture.Country).DefinitionHashes[Fixture.CustomerId],
            Fixture.Registry(Build(true), Fixture.Country).DefinitionHashes[Fixture.CustomerId]);
    }

    [Fact]
    public void EquivalentNumericDomainsAndDecimalSpellingsAreCanonicalized()
    {
        var inclusive = Fixture.WithField(new("rating", ScalarType.UInt8, defaultValue: new NumericValue(Fixture.Number("1.00")),
            constraints: new NumericConstraints(Minimum: Fixture.Number("1"))));
        var exclusive = Fixture.WithField(new("rating", ScalarType.UInt8, defaultValue: new NumericValue(Fixture.Number("1")),
            constraints: new NumericConstraints(Minimum: Fixture.Number("0"), ExclusiveMinimum: true)));
        Assert.Equal(Fixture.Registry(inclusive).DefinitionHashes[Fixture.CustomerId], Fixture.Registry(exclusive).DefinitionHashes[Fixture.CustomerId]);
        Assert.Equal(Fixture.Registry(Fixture.WithField(new("rating", ScalarType.UInt8))).DefinitionHashes[Fixture.CustomerId],
            Fixture.Registry(Fixture.WithField(new("rating", ScalarType.UInt8, constraints: new NumericConstraints(Fixture.Number("0"), Fixture.Number("255"), MultipleOf: Fixture.Number("1.0"))))).DefinitionHashes[Fixture.CustomerId]);
    }

    [Fact]
    public void EnumAndAnnotationInsertionOrderAreSetsButPresentationFieldOrderIsMeaningful()
    {
        ResourceDefinition Build(bool reverse, bool reverseSelection = false) => Fixture.Resource(
            [Fixture.Id, new("state", ScalarType.Enum, enumValues: reverse ? ["closed", "open"] : ["open", "closed"])],
            annotations: reverse
                ? [new("UI", "SelectionFields", new AnnotationFieldNames(reverseSelection ? ["state", "id"] : ["id", "state"])), new("Common", "Label", new AnnotationText("Test"))]
                : [new("Common", "Label", new AnnotationText("Test")), new("UI", "SelectionFields", new AnnotationFieldNames(["id", "state"]))]);
        string original = Fixture.Registry(Build(false)).DefinitionHashes[Fixture.CustomerId];
        Assert.Equal(original, Fixture.Registry(Build(true)).DefinitionHashes[Fixture.CustomerId]);
        Assert.NotEqual(original, Fixture.Registry(Build(true, true)).DefinitionHashes[Fixture.CustomerId]);
    }

    [Theory]
    [InlineData("es-ES")]
    [InlineData("tr-TR")]
    public void HashDoesNotDependOnCurrentCulture(string culture)
    {
        var original = CultureInfo.CurrentCulture;
        string expected = Fixture.Registry(Fixture.Customer, Fixture.Country).DefinitionHashes[Fixture.CustomerId];
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            Assert.Equal(expected, Fixture.Registry(Fixture.Customer, Fixture.Country).DefinitionHashes[Fixture.CustomerId]);
        }
        finally { CultureInfo.CurrentCulture = original; }
    }
}
