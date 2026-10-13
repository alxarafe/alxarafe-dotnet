using Alxarafe.Modularity;

namespace Alxarafe.Host;

// Opt-in descriptors enter the same pipeline as built-in modules, with no functional API.
public sealed class ConformanceModuleSource : IModuleSource
{
    public const string EnvironmentName = "Testing";
    public const string EnabledConfiguration = "Modules:ConformanceEnabled";
    public const string MathId = "erbas.dev.math";
    public const string StatisticsId = "erbas.dev.statistics";

    public IEnumerable<IAlxarafeModule> GetModules() =>
    [
        new DescriptorModule(new ModuleDescriptor(MathId, ModuleApi.CurrentVersion, [])),
        new DescriptorModule(new ModuleDescriptor(StatisticsId, ModuleApi.CurrentVersion, [MathId]))
    ];

    private sealed class DescriptorModule(ModuleDescriptor descriptor) : IAlxarafeModule
    {
        public ModuleDescriptor Descriptor { get; } = descriptor;
        public void ConfigureServices(IServiceCollection services) { }
        public Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
