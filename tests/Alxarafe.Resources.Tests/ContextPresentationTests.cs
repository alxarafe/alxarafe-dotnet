using System.Globalization;
using Alxarafe.Resources;
using Xunit;

namespace Alxarafe.Resources.Tests;

public sealed class ContextPresentationTests
{
    private const string Entity = "item.entity", Collection = "item.collection", Selection = "item.selection";
    private static readonly ActionDefinition[] Actions = [new(Entity, ActionScope.Entity, Entity),
        new(Collection, ActionScope.Collection, Collection), new(Selection, ActionScope.Selection, Selection, requiresSelection: true)];
    private static ResourceFormDefinition Form(params string[] fields) => new([new("general", [new("details", [new("identity", fields)])])]);
    private static ResourceDefinition Definition(ResourceContextPresentationDefinition? contextual,
        IEnumerable<FieldDefinition>? fields = null, ResourcePresentationDefinition? legacy = null) =>
        new(Fixture.CustomerId, Fixture.Version, Fixture.ModuleId, new("id"), fields ?? [Fixture.Id, new("name", ScalarType.String)], [], new([]),
            actions: Actions, presentation: legacy, contextualPresentation: contextual);
    private static string Hash(ResourceDefinition resource) => Fixture.Registry(resource).DefinitionHashes[resource.Id];

    [Fact]
    public void SubsetsOrderAndIndependentContextsDoNotChangeFieldSemantics()
    {
        var type = new FieldDefinition("typeId", ScalarType.Uuid, required: true, immutable: true);
        var name = new FieldDefinition("name", ScalarType.String, required: true, constraints: new TextConstraints(MaximumLength: 30));
        var contextual = new ResourceContextPresentationDefinition(list: new([new("name", "Display name"), new("typeId")], [Collection, Selection]),
            create: Form("typeId", "name"), edit: Form("id", "name", "typeId"), view: Form("name", "typeId", "id"));
        var resource = Definition(contextual, [Fixture.Id, type, name], new(Form("id", "name", "typeId")));
        var actual = Fixture.Registry(resource).Require(resource.Id);
        Assert.Same(contextual, actual.ContextualPresentation);
        Assert.Equal(["name", "typeId"], contextual.List!.Columns.Select(column => column.Field));
        Assert.Equal(["typeId", "name"], contextual.Create!.Tabs[0].Sections[0].Groups[0].Fields);
        Assert.True(type.Immutable); Assert.True(type.Required); Assert.False(type.ReadOnly);
        Assert.Same(name.Constraints, actual.Fields.Single(field => field.Name == name.Name).Constraints);
        Assert.Equal("Display name", contextual.List.Columns[0].Label);
    }
    [Fact]
    public void ExplicitEmptyContextsAreValidDistinctSnapshotsWithoutLegacyMerging()
    {
        var columns = new List<ResourceListColumn>(); var actions = new List<string>();
        var list = new ResourceListPresentationDefinition(columns, actions);
        var contextual = new ResourceContextPresentationDefinition(list, new([]), new([]), new([]));
        var resource = Definition(contextual, legacy: new(Form("id", "name")));
        Fixture.Registry(resource); columns.Add(new("name")); actions.Add(Collection);
        Assert.Empty(list.Columns); Assert.Empty(list.Actions); Assert.Empty(contextual.Create!.Tabs);
        Assert.Throws<NotSupportedException>(() => ((IList<ResourceListColumn>)list.Columns).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<string>)list.Actions).Clear());
        Assert.Null(Definition(null).ContextualPresentation);
        Assert.NotEqual(Hash(Definition(null)), Hash(Definition(new())));
        Assert.NotEqual(Hash(Definition(new())), Hash(Definition(new(create: new([])))));
    }
    [Theory]
    [InlineData("unknown-list")]
    [InlineData("duplicate-list")]
    [InlineData("unknown-field")]
    [InlineData("duplicate-field")]
    [InlineData("duplicate-across-groups")]
    [InlineData("duplicate-tab")]
    [InlineData("duplicate-section")]
    [InlineData("duplicate-group")]
    [InlineData("unknown-action")]
    [InlineData("duplicate-action")]
    [InlineData("create-entity")]
    [InlineData("create-selection")]
    [InlineData("edit-collection")]
    [InlineData("view-selection")]
    [InlineData("list-entity")]
    [InlineData("bad-label")]
    [InlineData("bad-list-label")]
    [InlineData("bad-policy")]
    public void InvalidDeclarationsAreRejectedBeforeServing(string fault)
    {
        var form = Form("name"); var tab = form.Tabs[0]; var section = tab.Sections[0];
        ResourceContextPresentationDefinition contextual = fault switch
        {
            "unknown-list" => new(list: new([new("missing")])),
            "duplicate-list" => new(list: new([new("name"), new("name", "Other label")])),
            "unknown-field" => new(create: Form("missing")),
            "duplicate-field" => new(create: Form("name", "name")),
            "duplicate-across-groups" => new(edit: new([new("general", [new("details", [new("first", ["name"]), new("second", ["name"])])])])),
            "duplicate-tab" => new(edit: new([tab, tab])),
            "duplicate-section" => new(edit: new([new("general", [section, section])])),
            "duplicate-group" => new(edit: new([new("general", [new("details", [new("same", []), new("same", [])])])])),
            "unknown-action" => new(view: new([], actions: ["item.unknown"])),
            "duplicate-action" => new(edit: new([new("general", [], actions: [Entity])], actions: [Entity])),
            "create-entity" => new(create: new([], actions: [Entity])),
            "create-selection" => new(create: new([], actions: [Selection])),
            "edit-collection" => new(edit: new([], actions: [Collection])),
            "view-selection" => new(view: new([], actions: [Selection])),
            "list-entity" => new(list: new([], [Entity])),
            "bad-label" => new(edit: new([new("general", [], "<component>")])),
            "bad-list-label" => new(list: new([new("name", "<component>")])),
            _ => new(create: new([], new((UnsavedChangesPolicy)999)))
        };
        var failure = Assert.Throws<ResourceValidationException>(() => Fixture.Registry(Definition(contextual)));
        Assert.Equal(ResourceValidationCode.InvalidPresentation, failure.Code);
        Assert.StartsWith("contextualPresentation.", failure.Path, StringComparison.Ordinal);
    }
    [Fact]
    public void AllExistingScopesWorkOnlyInTheirPermittedContextAndIdsAreLocal()
    {
        var contextual = new ResourceContextPresentationDefinition(new([new("name")], [Collection, Selection]),
            new([], actions: [Collection]), new([], actions: [Entity]), new([], actions: [Entity]));
        Fixture.Registry(Definition(contextual));
        Fixture.Registry(Definition(new(edit: new([new("one", [new("details", [new("identity", ["id"])])]),
            new("two", [new("details", [new("identity", ["name"])])])]))));
    }
    [Theory]
    [InlineData("Hidden")]
    [InlineData("FormExcluded")]
    public void StaticHintsRemainInFieldSemanticsAndPlacementsCannotOverwriteThem(string term)
    {
        var field = new FieldDefinition("name", ScalarType.String, immutable: true, annotations: [new("UI", term, new AnnotationBoolean(true))]);
        var resource = Definition(new(new([new("name")]), Form("name"), Form("name"), Form("name")), [Fixture.Id, field]);
        Fixture.Registry(resource);
        Assert.Same(field, resource.Fields[1]); Assert.True(field.Immutable);
        Assert.Equal(new AnnotationBoolean(true), Assert.Single(field.Annotations).Value);
        // Existing legacy completeness/exclusion validation is not relaxed.
        Assert.Throws<ResourceValidationException>(() => Fixture.Registry(Definition(resource.ContextualPresentation, [Fixture.Id, field], new(Form("id", "name")))));
    }
    [Theory]
    [InlineData("list-order")]
    [InlineData("list-label")]
    [InlineData("list-actions")]
    [InlineData("create")]
    [InlineData("edit")]
    [InlineData("view")]
    [InlineData("behavior")]
    public void EveryContextualChangeParticipatesInTheSingleDefinitionHash(string change)
    {
        ResourceContextPresentationDefinition Build(bool changed) => new(
            new(changed && change == "list-order" ? [new("name"), new("id")] : [new("id"), new("name", changed && change == "list-label" ? "Changed" : null)], changed && change == "list-actions" ? [Collection] : []),
            changed && change == "create" ? Form("name") : Form("id"),
            changed && change == "edit" ? Form("name") : Form("id"),
            changed && change == "view" ? Form("name") : new(Form("id").Tabs, new(changed && change == "behavior" ? UnsavedChangesPolicy.AllowDiscard : UnsavedChangesPolicy.ConfirmDiscard)));
        Assert.NotEqual(Hash(Definition(Build(false))), Hash(Definition(Build(true))));
        var previous = CultureInfo.CurrentCulture;
        try { string expected = Hash(Definition(Build(false))); CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR"); Assert.Equal(expected, Hash(Definition(Build(false)))); }
        finally { CultureInfo.CurrentCulture = previous; }
    }
    [Fact]
    public void ContextualCanonicalV4GoldenVectorIsStableWhileLegacyV3RemainsUnchanged()
    {
        // Independently computed ordered UTF-8 v4 projection: one UUID field and an explicit empty CREATE form.
        const string expected = "d990424b22e635da454af5535dc730abd7dfc8140b8b961d684a446f1758a041";
        var resource = new ResourceDefinition(Fixture.CustomerId, Fixture.Version, Fixture.ModuleId, new("id"), [Fixture.Id], [], new([]), contextualPresentation: new(create: new([])));
        Assert.Equal(expected, Hash(resource));
    }
}
