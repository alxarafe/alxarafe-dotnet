using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;
using Alxarafe.Host;
using Alxarafe.Modules.Ai;
using Alxarafe.Modules.Assets;
using Alxarafe.Modules.Wms;
using Alxarafe.Modules.Wms.Application;
using Alxarafe.Modules.Wms.Domain;
using Xunit;

namespace Alxarafe.ArchitectureTests;

public sealed class HostBoundaryTests
{
    private const string ModulePrefix = "Alxarafe.Modules.";
    private static readonly string[] EntryAssemblies = ["Alxarafe.Modules.Ai.Module", "Alxarafe.Modules.Assets.Module", "Alxarafe.Modules.Wms.Module"];
    private static readonly string[] EntryProjects =
    [
        "../Modules/Ai/Alxarafe.Modules.Ai.Module/Alxarafe.Modules.Ai.Module.csproj",
        "../Modules/Assets/Alxarafe.Modules.Assets.Module/Alxarafe.Modules.Assets.Module.csproj",
        "../Modules/Wms/Alxarafe.Modules.Wms.Module/Alxarafe.Modules.Wms.Module.csproj"
    ];
    private static readonly string[] AllowedModuleUsings = ["using Alxarafe.Modules.Ai;", "using Alxarafe.Modules.Assets;", "using Alxarafe.Modules.Wms;"];
    private static readonly string[] AllowedEntryTypeLines =
    [
        "var modules = new ModuleBuilder(builder.Services).AddModulesFromAssemblies(typeof(WmsModule).Assembly, typeof(AiModule).Assembly, typeof(AssetsModule).Assembly);",
        "AiModule.UseInvocationAudit(app);"
    ];

    [Fact]
    public void FrameworkAssembliesHaveNoModuleReferences()
    {
        Type[] markers =
        [
            typeof(Alxarafe.Core.AssemblyMarker), typeof(Alxarafe.Modularity.ModuleRuntime),
            typeof(Alxarafe.AspNetCore.IHttpModule), typeof(Alxarafe.EntityFrameworkCore.AssemblyMarker),
            typeof(Alxarafe.Security.PermissionDefinition), typeof(Alxarafe.Security.AspNetCore.PermissionRequirement),
            typeof(Alxarafe.Security.EntityFrameworkCore.SecurityDbContext)
        ];
        foreach (var marker in markers)
        {
            Assert.DoesNotContain(marker.Assembly.GetReferencedAssemblies(),
                reference => reference.Name?.StartsWith(ModulePrefix, StringComparison.Ordinal) == true);
            string assemblyName = marker.Assembly.GetName().Name!;
            var project = XDocument.Load(Path.Combine(RepositoryRoot, "src", assemblyName, assemblyName + ".csproj"));
            Assert.DoesNotContain(project.Descendants("ProjectReference"),
                reference => reference.Attribute("Include")!.Value.Contains("Modules/", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void HostReferencesOnlyModuleEntryAssembliesAndProjects()
    {
        var references = typeof(PlatformExceptionHandler).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name).Where(name => name?.StartsWith(ModulePrefix, StringComparison.Ordinal) == true)
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(EntryAssemblies, references);
        var project = XDocument.Load(Path.Combine(RepositoryRoot, "src/Alxarafe.Host/Alxarafe.Host.csproj"));
        var moduleProjects = project.Descendants("ProjectReference").Select(reference => reference.Attribute("Include")!.Value)
            .Where(path => path.Contains("Modules/", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(EntryProjects, moduleProjects);
    }

    [Fact]
    public void HostKnowsOnlyTheEntryTypeForExplicitComposition()
    {
        using var stream = File.OpenRead(typeof(PlatformExceptionHandler).Assembly.Location);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var types = metadata.TypeReferences.Select(handle => metadata.GetTypeReference(handle))
            .Select(type => $"{metadata.GetString(type.Namespace)}.{metadata.GetString(type.Name)}")
            .Where(name => name.StartsWith(ModulePrefix, StringComparison.Ordinal));
        Assert.Equal(new[] { typeof(AiModule).FullName!, typeof(AssetsModule).FullName!, typeof(WmsModule).FullName! }, types.Order(StringComparer.Ordinal));
        foreach (var file in SourceFiles(Path.Combine(RepositoryRoot, "src/Alxarafe.Host")))
        {
            foreach (var line in File.ReadLines(file).Where(line => line.Contains(ModulePrefix, StringComparison.Ordinal)))
            {
                Assert.Equal("Program.cs", Path.GetFileName(file));
                Assert.Contains(line, AllowedModuleUsings);
            }
            foreach (var line in File.ReadLines(file).Where(line => line.Contains(nameof(AiModule), StringComparison.Ordinal)
                || line.Contains(nameof(AssetsModule), StringComparison.Ordinal) || line.Contains(nameof(WmsModule), StringComparison.Ordinal)))
            {
                Assert.Equal("Program.cs", Path.GetFileName(file));
                Assert.Contains(line, AllowedEntryTypeLines);
            }
        }
    }

    [Fact]
    public void FrameworkSourcesAndResourcesHaveNoModuleDomainKnowledge()
    {
        var projects = Directory.GetDirectories(Path.Combine(RepositoryRoot, "src"))
            .Where(path => Path.GetFileName(path).StartsWith("Alxarafe.", StringComparison.Ordinal)
                && Path.GetFileName(path) != "Alxarafe.Host");
        foreach (var file in projects.SelectMany(SourceFiles))
            Assert.False(File.ReadAllText(file).Contains(ModulePrefix, StringComparison.OrdinalIgnoreCase),
                $"{file} references a business module.");
    }

    [Fact]
    public void WmsDomainAndApplicationHaveNoFrameworkOrAdapterDependencies()
    {
        foreach (var assembly in new[] { typeof(Uom).Assembly, typeof(UomCatalogue).Assembly })
        {
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                string name = reference.Name!;
                Assert.True(name.StartsWith("System.", StringComparison.Ordinal)
                    || name == typeof(Uom).Assembly.GetName().Name, $"Unexpected pure-layer reference: {name}");
            }
        }
        foreach (string layer in new[] { "Domain", "Application" })
        {
            var project = XDocument.Load(Path.Combine(RepositoryRoot, $"src/Modules/Wms/Alxarafe.Modules.Wms.{layer}/Alxarafe.Modules.Wms.{layer}.csproj"));
            Assert.Empty(project.Descendants("FrameworkReference"));
            Assert.Empty(project.Descendants("PackageReference"));
            Assert.All(project.Descendants("ProjectReference"), reference =>
                Assert.Equal("../Alxarafe.Modules.Wms.Domain/Alxarafe.Modules.Wms.Domain.csproj", reference.Attribute("Include")!.Value));
        }
    }

    private static IEnumerable<string> SourceFiles(string directory) =>
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Where(path => Path.GetExtension(path) is ".cs" or ".resx");

    internal static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Alxarafe.sln")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("Architecture tests must run from the repository.");
        }
    }
}
