using Alxarafe.Modularity;

namespace Alxarafe.Host;

// Opt-in descriptors enter the same pipeline as built-in modules, with no functional API.
public sealed class ConformanceModuleSource : IModuleSource
{
    private const string Version = "1.0.0";
    public const string EnvironmentName = "Testing";
    public const string EnabledConfiguration = "Modules:ConformanceEnabled";
    public const string MathId = "erbas.dev.math";
    public const string StatisticsId = "erbas.dev.statistics";

    public IEnumerable<IAlxarafeModule> GetModules() =>
    [
        new DescriptorModule(new ModuleDescriptor(MathId, Version, ModuleApi.CurrentVersion, [])),
        new DescriptorModule(new ModuleDescriptor(StatisticsId, Version, ModuleApi.CurrentVersion, [MathId]))
    ];

    private sealed class DescriptorModule(ModuleDescriptor descriptor) : IAlxarafeModule
    {
        public ModuleDescriptor Descriptor { get; } = descriptor;
    }
}
