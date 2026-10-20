using Alxarafe.Modularity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class CapabilityRegistryTests
{
    private const string Schema = "#/components/schemas/Fixture";
    private static CapabilityDescriptor Valid => new("fixture.items.list", "fixture", "1.0.0", "Name", "Description",
        "read", new(Schema), new(Schema), ["fixture.items.read"]);

    [Fact]
    public void RegistrationRejectsDuplicateIdsAndInvalidDescriptors()
    {
        Assert.Throws<InvalidOperationException>(() => Registry(Valid, Valid));
        foreach (var invalid in new[]
        {
            Valid with { Id = "bad" }, Valid with { Module = "other" }, Valid with { Version = "01.0.0" },
            Valid with { Name = " " }, Valid with { Effect = "unknown" },
            Valid with { OutputSchema = new("#/components/schemas/Missing") },
            Valid with { RequiredPermissions = ["z", "a"] }, Valid with { RequiredPermissions = ["a", "a"] }
        })
            Assert.Throws<InvalidOperationException>(() => Registry(invalid));
    }

    private static CapabilityRegistry Registry(params CapabilityDescriptor[] descriptors) =>
        new(new ModuleRegistry([new Source(new Provider(descriptors))]));

    private sealed record Source(IAlxarafeModule Module) : IModuleSource
    {
        public IEnumerable<IAlxarafeModule> GetModules() => [Module];
    }
    private sealed class Provider(CapabilityDescriptor[] descriptors) : IAlxarafeModule, ICapabilityProvider
    {
        public ModuleDescriptor Descriptor { get; } = new("fixture", ModuleApi.CurrentVersion, []);
        public IEnumerable<CapabilityDescriptor> GetCapabilities() => descriptors;
        public IEnumerable<string> GetSchemaReferences() => [Schema];
        public void ConfigureServices(IServiceCollection services) { }
        public Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
