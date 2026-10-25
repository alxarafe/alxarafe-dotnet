using Alxarafe.Modularity;
using Alxarafe.Resources;

namespace Alxarafe.Resources.Tests;

internal static class Fixture
{
    internal const string ModuleId = "fixture.module";
    internal const string OtherModuleId = "fixture.other";
    internal const string Version = "1.0.0";
    internal const string CustomerId = "fixture.customer";
    internal const string CountryId = "fixture.country";
    internal const string ReadPermission = "customer.read";
    internal static ResourceNumber Number(string value) => ResourceNumber.Parse(value);
    internal static FieldDefinition Id => new("id", ScalarType.Uuid, readOnly: true, immutable: true);
    internal static ModuleRegistry Modules(params IAlxarafeModule[] modules) => new([new Source(modules.Length == 0 ? [new Module(ModuleId), new Module(OtherModuleId)] : modules)]);
    internal static ResourceRegistry Registry(params ResourceDefinition[] resources) => new(Modules(), [new Provider(resources)]);
    internal static ResourceDefinition Resource(IEnumerable<FieldDefinition>? fields = null,
        IEnumerable<ResourceOperation>? operations = null, ResourceSecurity? security = null,
        IEnumerable<RelationDefinition>? relations = null, IEnumerable<ActionDefinition>? actions = null,
        IEnumerable<Annotation>? annotations = null, string id = CustomerId, string moduleId = ModuleId,
        string version = Version, IdDefinition? idDefinition = null) =>
        new(id, version, moduleId, idDefinition ?? new("id"), fields ?? [Id], operations ?? [ResourceOperation.Get],
            security ?? new([new(ResourceOperation.Get, [ReadPermission])]), relations, actions, annotations);
    internal static ResourceDefinition WithField(FieldDefinition field) => Resource([Id, field]);
    internal static ResourceDefinition Country => Resource([Id, new("name", ScalarType.String, required: true)], id: CountryId);
    internal static ResourceDefinition Customer => Resource(
        [Id, new("code", ScalarType.String, required: true, constraints: new TextConstraints(MaximumLength: 30), capabilities: new(true, true)),
            new("name", ScalarType.String, required: true, constraints: new TextConstraints(MaximumLength: 200), capabilities: new(true, true, true)),
            new("creditRating", ScalarType.UInt8, constraints: new NumericConstraints(Number("0"), Number("10"))),
            new("active", ScalarType.Boolean, defaultValue: new BooleanValue(true), capabilities: new(true)),
            new("countryId", ScalarType.Uuid, nullable: true)],
        [ResourceOperation.List, ResourceOperation.Get, ResourceOperation.Create, ResourceOperation.Update],
        new([new(ResourceOperation.List, [ReadPermission]), new(ResourceOperation.Get, [ReadPermission]),
            new(ResourceOperation.Create, ["customer.create"]), new(ResourceOperation.Update, ["customer.update"])]),
        [new("country", RelationKind.ManyToOne, CountryId, "countryId", "id", "name")]);

    internal sealed record Source(IReadOnlyCollection<IAlxarafeModule> Values) : IModuleSource
    {
        public IEnumerable<IAlxarafeModule> GetModules() => Values;
    }
    internal sealed class Module(string id) : IAlxarafeModule
    {
        public ModuleDescriptor Descriptor { get; set; } = new(id, Version, ModuleApi.CurrentVersion, []);
    }
    internal sealed class Provider(IReadOnlyCollection<ResourceDefinition> resources) : IResourceDefinitionProvider
    {
        internal int Calls { get; private set; }
        public IReadOnlyCollection<ResourceDefinition> GetResourceDefinitions() { Calls++; return resources; }
    }
    internal sealed class ContributingModule(string id, IReadOnlyCollection<ResourceDefinition> resources) : IAlxarafeModule, IResourceDefinitionProvider
    {
        public ModuleDescriptor Descriptor { get; set; } = new(id, Version, ModuleApi.CurrentVersion, []);
        public IReadOnlyCollection<ResourceDefinition> GetResourceDefinitions() => resources;
    }
}
