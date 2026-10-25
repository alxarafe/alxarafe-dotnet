using System.Globalization;
using Alxarafe.Resources;
using Alxarafe.ResourceTestFixtures;
using Xunit;

namespace Alxarafe.Resources.Tests;

public sealed class FormPresentationTests
{
    private static ResourceFormDefinition Single(params string[] fields) => new([new("general", [new("details", [new("basic", fields)])])]);
    private static ResourceDefinition Minimal(ResourceFormDefinition form, IEnumerable<FieldDefinition>? fields = null, IEnumerable<ActionDefinition>? actions = null) =>
        new(Fixture.CustomerId, Fixture.Version, Fixture.ModuleId, new("id"), fields ?? [Fixture.Id], [], new([]), actions: actions, presentation: new(form));
    private static ResourceFormTab Tab(ResourceFormTab source, IEnumerable<ResourceFormSection> sections) => new(source.Id, sections, source.Label, source.Actions);
    private static ResourceFormSection Section(ResourceFormSection source, IEnumerable<ResourceFormFieldGroup> groups) => new(source.Id, groups, source.Label, source.Collapsible, source.InitiallyCollapsed, source.Actions);
    private static string Hash(ResourceDefinition definition) => Fixture.Registry(definition).DefinitionHashes[definition.Id];
    [Fact]
    public void SingleAndComplexLayoutsValidateAndRetainDeclaredOrder()
    {
        var minimal = Minimal(Single("id"));
        Assert.Equal(UnsavedChangesPolicy.ConfirmDiscard, Fixture.Registry(minimal).Require(minimal.Id).Presentation!.Form.Behavior.UnsavedChanges);
        var definition = ComplexFormFixture.Create(); var form = Fixture.Registry(definition).Require(definition.Id).Presentation!.Form;
        Assert.Equal(["general", "logistics", "inventory", "configuration", "audit"], form.Tabs.Select(tab => tab.Id));
        Assert.Equal(["identity", "description"], form.Tabs[0].Sections.Select(section => section.Id));
        Assert.Equal(["code", "name"], form.Tabs[0].Sections[0].Groups[0].Fields);
        Assert.Equal(30, form.Tabs.SelectMany(tab => tab.Sections).SelectMany(section => section.Groups).Sum(group => group.Fields.Count));
        Assert.Contains(definition.Fields, field => field.Name == "quantity" && field.ReadOnly);
        Assert.Contains(definition.Fields, field => field.Name == "code" && field.Immutable && field.Required);
    }
    [Fact]
    public void CollectionsAreDeepSnapshotsWithoutMutationApis()
    {
        var names = new List<string> { "id" }; var actions = new List<string>();
        var groups = new List<ResourceFormFieldGroup> { new("basic", names) };
        var sections = new List<ResourceFormSection> { new("details", groups) };
        var tabs = new List<ResourceFormTab> { new("general", sections) };
        var form = new ResourceFormDefinition(tabs, actions: actions);
        names.Clear(); groups.Clear(); sections.Clear(); tabs.Clear(); actions.Add("unknown");
        Fixture.Registry(Minimal(form)); Assert.Equal("id", Assert.Single(form.Tabs[0].Sections[0].Groups[0].Fields)); Assert.Empty(form.Actions);
        Assert.Throws<NotSupportedException>(() => ((IList<ResourceFormTab>)form.Tabs).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<ResourceFormSection>)form.Tabs[0].Sections).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<ResourceFormFieldGroup>)form.Tabs[0].Sections[0].Groups).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<string>)form.Tabs[0].Sections[0].Groups[0].Fields).Clear());
    }
    [Theory]
    [InlineData("unknown-field")]
    [InlineData("duplicate-field")]
    [InlineData("duplicate-across-groups")]
    [InlineData("missing-visible-field")]
    [InlineData("unknown-action")]
    [InlineData("duplicate-action")]
    [InlineData("duplicate-tab")]
    [InlineData("duplicate-section")]
    [InlineData("duplicate-group")]
    [InlineData("empty-tabs")]
    [InlineData("empty-sections")]
    [InlineData("empty-groups")]
    [InlineData("empty-fields")]
    [InlineData("bad-id")]
    [InlineData("bad-label")]
    [InlineData("long-label")]
    [InlineData("long-id")]
    [InlineData("collapsed-without-collapse")]
    [InlineData("invalid-policy")]
    public void InvalidLayoutsFailAtRegistryConstruction(string invalid)
    {
        var form = invalid switch
        {
            "unknown-field" => Single("unknown"),
            "duplicate-field" => Single("id", "id"),
            "missing-visible-field" => Single("name"),
            "duplicate-across-groups" => new ResourceFormDefinition([new("general", [new("details", [new("first", ["id"]), new("second", ["id"])])])]),
            "unknown-action" => new(Single("id").Tabs, actions: ["unknown"]),
            "duplicate-action" => new([new("general", [new("details", [new("basic", ["id"])])], actions: ["item.post"])], actions: ["item.post"]),
            "duplicate-tab" => new([Single("id").Tabs[0], Single("id").Tabs[0]]),
            "duplicate-section" => new([new("general", [Single("id").Tabs[0].Sections[0], Single("id").Tabs[0].Sections[0]])]),
            "duplicate-group" => new([new("general", [new("details", [new("basic", ["id"]), new("basic", ["id"])])])]),
            "empty-tabs" => new([]),
            "empty-sections" => new([new("general", [])]),
            "empty-groups" => new([new("general", [new("details", [])])]),
            "empty-fields" => Single(),
            "bad-id" => new([new("bad id", Single("id").Tabs[0].Sections)]),
            "bad-label" => new([new("general", Single("id").Tabs[0].Sections, "<b>General</b>")]),
            "long-label" => new([new("general", Single("id").Tabs[0].Sections, new string('x', ResourceFormDefinition.MaximumLabelLength + 1))]),
            "long-id" => new([new(new string('x', ResourceFormDefinition.MaximumNodeIdLength + 1), Single("id").Tabs[0].Sections)]),
            "collapsed-without-collapse" => new([new("general", [new("details", [new("basic", ["id"])], initiallyCollapsed: true)])]),
            _ => new(Single("id").Tabs, new((UnsavedChangesPolicy)999))
        };
        var fields = invalid == "missing-visible-field" ? new[] { Fixture.Id, new FieldDefinition("name", ScalarType.String) } : [Fixture.Id];
        var failure = Assert.Throws<ResourceValidationException>(() => Fixture.Registry(Minimal(form, fields, [new("item.post", ActionScope.Entity, "item.post")])));
        Assert.Equal(ResourceValidationCode.InvalidPresentation, failure.Code); Assert.StartsWith("presentation.form.", failure.Path, StringComparison.Ordinal);
    }
    [Theory]
    [InlineData("Hidden")]
    [InlineData("FormExcluded")]
    public void ExclusionMustBeExplicitAndTrueAndExcludedFieldsCannotBePlaced(string term)
    {
        var excluded = new FieldDefinition("internalValue", ScalarType.String, annotations: [new("UI", term, new AnnotationBoolean(true))]);
        Fixture.Registry(Minimal(Single("id"), [Fixture.Id, excluded]));
        Assert.Throws<ResourceValidationException>(() => Fixture.Registry(Minimal(Single("id", excluded.Name), [Fixture.Id, excluded])));
        var visible = new FieldDefinition("internalValue", ScalarType.String, annotations: [new("UI", term, new AnnotationBoolean(false))]);
        Assert.Throws<ResourceValidationException>(() => Fixture.Registry(Minimal(Single("id"), [Fixture.Id, visible])));
        Fixture.Registry(Minimal(Single("id", visible.Name), [Fixture.Id, visible]));
    }
    [Fact]
    public void ExclusionAnnotationsRemainTypedAndActionPlacementIsOptional()
    {
        Assert.Throws<ResourceValidationException>(() => Fixture.Registry(Minimal(Single("id"), [Fixture.Id, new("value", ScalarType.String, annotations: [new("UI", "FormExcluded", new AnnotationText("true"))])])));
        Fixture.Registry(Minimal(Single("id"), actions: [new("item.post", ActionScope.Entity, "item.post")]));
        // IDs are local to their parent scope; repeated group/section IDs in another tab are unambiguous.
        Fixture.Registry(Minimal(new([new("first", [new("details", [new("basic", ["id"])])]), new("second", [new("details", [new("basic", ["name"])])])]), [Fixture.Id, new("name", ScalarType.String)]));
    }
    [Theory]
    [InlineData("tab-added")]
    [InlineData("tab-order")]
    [InlineData("section-order")]
    [InlineData("group-order")]
    [InlineData("field-movement")]
    [InlineData("field-order")]
    [InlineData("action-placement")]
    [InlineData("policy")]
    [InlineData("label")]
    [InlineData("collapsed")]
    public void EveryMeaningfulPresentationChangeAltersHash(string change)
    {
        var form = ComplexFormFixture.Form(); var tabs = form.Tabs.ToArray(); var actions = form.Actions; var behavior = form.Behavior;
        switch (change)
        {
            case "tab-added":
                var first = tabs[0]; tabs[0] = Tab(first, first.Sections.Take(1)); tabs = tabs.Append(new ResourceFormTab("extra", first.Sections.Skip(1))).ToArray(); break;
            case "tab-order": tabs = tabs.Reverse().ToArray(); break;
            case "section-order": tabs[0] = Tab(tabs[0], tabs[0].Sections.Reverse()); break;
            case "group-order": tabs[0] = Tab(tabs[0], tabs[0].Sections.Select((section, index) => index == 0 ? Section(section, section.Groups.Reverse()) : section)); break;
            case "field-movement":
                tabs = tabs.Select(tab => Tab(tab, tab.Sections.Select(section => Section(section, section.Groups.Select(group => new ResourceFormFieldGroup(group.Id, group.Fields.Select(field => field == "code" ? "active" : field == "active" ? "code" : field), group.Label)))))).ToArray(); break;
            case "field-order": tabs[0] = Tab(tabs[0], tabs[0].Sections.Select((section, index) => index == 0 ? Section(section, section.Groups.Select((group, groupIndex) => groupIndex == 0 ? new ResourceFormFieldGroup(group.Id, group.Fields.Reverse(), group.Label) : group)) : section)); break;
            case "action-placement": actions = []; tabs[0] = new(tabs[0].Id, tabs[0].Sections, tabs[0].Label, [ComplexFormFixture.Duplicate]); break;
            case "policy": behavior = new(UnsavedChangesPolicy.AllowDiscard); break;
            case "label": tabs[0] = new(tabs[0].Id, tabs[0].Sections, "Identification", tabs[0].Actions); break;
            case "collapsed": tabs[2] = Tab(tabs[2], tabs[2].Sections.Select(section => new ResourceFormSection(section.Id, section.Groups, section.Label, section.Collapsible, false, section.Actions))); break;
        }
        Assert.NotEqual(Hash(ComplexFormFixture.Create()), Hash(ComplexFormFixture.Create(form: new(tabs, behavior, actions))));
    }
    [Fact]
    public void DefinitionInsertionOrderAndCultureAreIrrelevantButAbsenceOfPresentationIsDistinct()
    {
        string expected = Hash(ComplexFormFixture.Create()); var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            Assert.Equal(expected, Hash(ComplexFormFixture.Create(reverseDefinitions: true)));
        }
        finally { CultureInfo.CurrentCulture = previous; }
        Assert.NotEqual(expected, Hash(ComplexFormFixture.Create(includePresentation: false)));
        Assert.Null(Fixture.Registry(ComplexFormFixture.Create(includePresentation: false)).Require(ComplexFormFixture.Id).Presentation);
    }
}
