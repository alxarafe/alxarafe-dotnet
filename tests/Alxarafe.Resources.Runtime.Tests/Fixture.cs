using Alxarafe.Core;
using Alxarafe.Modularity;
using Alxarafe.Resources;
using Alxarafe.Resources.Runtime;

namespace Alxarafe.Resources.Runtime.Tests;

internal sealed class Fixture
{
    internal const string ResourceId = "fixture.customer";
    internal const string OtherId = "fixture.other";
    internal const string ModuleId = "fixture.module";
    internal const string Version = "1.0.0";
    internal const string Read = "customer.read";
    internal const string Create = "customer.create";
    internal const string Update = "customer.update";
    internal const string Delete = "customer.delete";
    internal static readonly Guid IdentifierValue = Guid.Parse("f0aab454-b6b2-4039-91e6-d0a14404c22e");
    internal static ResourceIdentifier Id => new("id", new UuidValue(IdentifierValue));
    internal ResourceRegistry Metadata { get; }
    internal ModuleRegistry Modules { get; }
    internal StateStore States { get; } = new();
    internal TenantContext Tenant { get; } = new();
    internal AuthorizationAdapter Authorization { get; } = new();
    internal MemoryRepository Repository { get; } = new(ResourceId);
    internal ResourceRepositoryCatalog Catalog { get; }
    internal ResourceRuntime Runtime { get; }
    internal static ResourceRecord Record(params (string Name, ScalarValue Value)[] values) => new(values.Select(pair => new KeyValuePair<string, ScalarValue>(pair.Name, pair.Value)));
    internal static ResourceRecord Input => Record(("code", new TextValue("C-1")), ("name", new TextValue("Customer")));
    internal static ResourceRecord Persisted => Record(("id", Id.Value), ("code", new TextValue("C-1")), ("name", new TextValue("Customer")), ("active", new BooleanValue(true)));

    internal Fixture(bool delete = false, ModulePersistence persistence = ModulePersistence.Tenant,
        ResourceRepositoryScope? scope = null, bool repository = true, bool searchable = true,
        IEnumerable<IResourceOperationValidator>? validators = null)
    {
        Modules = new([new Source([new Module(persistence)])]);
        Metadata = new(Modules, [new Provider([Definition(delete, searchable: searchable)])]);
        Catalog = new(Metadata, Modules, [new(ResourceId, scope ?? (persistence == ModulePersistence.Tenant ? ResourceRepositoryScope.Tenant : ResourceRepositoryScope.Platform))]);
        var registry = new ResourceRepositoryRegistry(Metadata, Catalog, repository ? [Repository] : [], validators ?? []);
        Runtime = new(Metadata, new ModuleStateService(Modules, States), Catalog, registry, Tenant, Authorization);
    }

    internal static ResourceDefinition Definition(bool delete = false, string id = ResourceId, bool searchable = true)
    {
        var operations = new List<ResourceOperation> { ResourceOperation.List, ResourceOperation.Get, ResourceOperation.Create, ResourceOperation.Update };
        if (delete) operations.Add(ResourceOperation.Delete);
        return new(id, Version, ModuleId, new("id"),
        [new("id", ScalarType.Uuid, readOnly: true, immutable: true),
            new("code", ScalarType.String, required: true, constraints: new TextConstraints(MaximumLength: 30), capabilities: new(true, true)),
            new("name", ScalarType.String, required: true, constraints: new TextConstraints(MaximumLength: 200), capabilities: new(true, true, searchable)),
            new("byteValue", ScalarType.UInt8),
            new("creditRating", ScalarType.UInt8, constraints: new NumericConstraints(ResourceNumber.Zero, ResourceNumber.Parse("10"))),
            new("active", ScalarType.Boolean, defaultValue: new BooleanValue(true), capabilities: new(true)),
            new("optional", ScalarType.String, nullable: true, defaultValue: new TextValue("fallback")), new("fixed", ScalarType.String, immutable: true),
            new("computed", ScalarType.String, readOnly: true), new("state", ScalarType.Enum, enumValues: ["open", "closed"])],
            operations, new(operations.Select(operation => new OperationSecurityRule(operation, [operation switch
            {
                ResourceOperation.Create => Create, ResourceOperation.Update => Update, ResourceOperation.Delete => Delete, _ => Read
            }]))));
    }
    internal sealed record Source(IReadOnlyCollection<IAlxarafeModule> Values) : IModuleSource
    { public IEnumerable<IAlxarafeModule> GetModules() => Values; }
    internal sealed class Module(ModulePersistence persistence) : IAlxarafeModule
    { public ModuleDescriptor Descriptor { get; } = new(ModuleId, Version, ModuleApi.CurrentVersion, [], persistence); }
    internal sealed class Provider(IReadOnlyCollection<ResourceDefinition> values) : IResourceDefinitionProvider
    { public IReadOnlyCollection<ResourceDefinition> GetResourceDefinitions() => values; }
    internal sealed class StateStore : IModuleStateStore
    {
        internal bool Enabled { get; set; } = true;
        public Task<bool?> FindAsync(string moduleId, CancellationToken cancellationToken = default) => Task.FromResult<bool?>(Enabled);
        public Task UpdateAsync(Action<IDictionary<string, bool>> change, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    internal sealed class TenantContext : IRequestTenantContext
    {
        internal bool Available { get; set; } = true;
        internal int Calls { get; private set; }
        public string? TenantId => Available ? "authoritative-fixture-tenant" : null;
        public Task<Tenant> RequireAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Available ? Task.FromResult(new Tenant(TenantId!, TenantId!, "Fixture", true, "fixture"))
                : Task.FromException<Tenant>(new TenantAccessException(TenantAccessException.Required));
        }
    }
    internal sealed class AuthorizationAdapter : IResourceAuthorization
    {
        internal HashSet<string> Permissions { get; } = new([Read, Create, Update, Delete], StringComparer.Ordinal);
        internal int Calls { get; private set; }
        public Task<bool> IsAllowedAsync(IReadOnlyList<string> requiredPermissions, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(requiredPermissions.All(Permissions.Contains)); }
    }
    internal sealed class Validator(string id, List<string> calls, bool block = false) : IResourceOperationValidator
    {
        public string ResourceId => Fixture.ResourceId;
        public string Id => id;
        internal ResourceOperationContext? Context { get; private set; }
        public Task<IReadOnlyList<ResourceIssue>> ValidateAsync(ResourceOperationContext context, CancellationToken cancellationToken = default)
        {
            Context = context; calls.Add(Id);
            return Task.FromResult<IReadOnlyList<ResourceIssue>>(block ? [new("business", ResourceInputError.BusinessRule, "Business rule rejected the operation.")] : []);
        }
    }
}
