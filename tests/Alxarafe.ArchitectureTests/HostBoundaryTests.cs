using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;
using Alxarafe.Host;
using Xunit;

namespace Alxarafe.ArchitectureTests;

public sealed class HostBoundaryTests
{
    [Fact]
    public void FrameworkAssembliesHaveNoModuleReferences()
    {
        Type[] markers =
        [
            typeof(Alxarafe.Core.AssemblyMarker),
            typeof(Alxarafe.Modularity.ModuleRuntime),
            typeof(Alxarafe.AspNetCore.IHttpModule),
            typeof(Alxarafe.EntityFrameworkCore.AssemblyMarker),
            typeof(Alxarafe.Security.PermissionDefinition),
            typeof(Alxarafe.Security.AspNetCore.PermissionRequirement),
            typeof(Alxarafe.Security.EntityFrameworkCore.SecurityDbContext)
        ];
        foreach (var marker in markers)
            Assert.DoesNotContain(marker.Assembly.GetReferencedAssemblies(),
                reference => reference.Name?.StartsWith("Alxarafe.Modules.", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void HostOnlyReferencesStaticModuleEntryAssembly()
    {
        var references = typeof(PlatformExceptionHandler).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name?.StartsWith("Alxarafe.Modules.", StringComparison.Ordinal) == true);
        Assert.Equal(["Alxarafe.Modules.Catalog.Module"], references);

        var project = XDocument.Load(Path.Combine(RepositoryRoot, "src/Alxarafe.Host/Alxarafe.Host.csproj"));
        var moduleProjects = project.Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")!.Value)
            .Where(path => path.Contains("Modules/", StringComparison.Ordinal));
        Assert.Equal(["../Modules/Catalog/Alxarafe.Modules.Catalog.Module/Alxarafe.Modules.Catalog.Module.csproj"], moduleProjects);
    }

    [Fact]
    public void HostOnlyUsesStaticModuleEntryType()
    {
        using var stream = File.OpenRead(typeof(PlatformExceptionHandler).Assembly.Location);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var types = metadata.TypeReferences.Select(handle => metadata.GetTypeReference(handle))
            .Select(type => $"{metadata.GetString(type.Namespace)}.{metadata.GetString(type.Name)}")
            .Where(name => name.StartsWith("Alxarafe.Modules.", StringComparison.Ordinal));
        Assert.Equal(["Alxarafe.Modules.Catalog.ModuleDefinition.CatalogModule"], types);

        // Compiled references do not detect unused using directives.
        foreach (var file in SourceFiles(Path.Combine(RepositoryRoot, "src/Alxarafe.Host")))
        {
            foreach (var line in File.ReadLines(file).Where(line => line.Contains("Alxarafe.Modules.", StringComparison.Ordinal)))
            {
                Assert.Equal("Program.cs", Path.GetFileName(file));
                Assert.Equal("using Alxarafe.Modules.Catalog.ModuleDefinition;", line.Trim());
            }
        }
    }

    [Fact]
    public void PlatformSourcesAndResourcesHaveNoCatalogDomainKnowledge()
    {
        var projects = Directory.GetDirectories(Path.Combine(RepositoryRoot, "src"))
            .Where(path => Path.GetFileName(path).StartsWith("Alxarafe.", StringComparison.Ordinal));
        string[] forbidden = ["sku", "ItemAlreadyExistsException", "CatalogMessages", "CatalogPermissions", "catalog."];
        foreach (var file in projects.SelectMany(SourceFiles))
        {
            var source = File.ReadAllText(file);
            if (Path.GetFileName(file) == "Program.cs")
                source = source.Replace("using Alxarafe.Modules.Catalog.ModuleDefinition;", string.Empty, StringComparison.Ordinal);
            foreach (var concept in forbidden)
                Assert.False(source.Contains(concept, StringComparison.OrdinalIgnoreCase), $"{file} contains module concept '{concept}'.");
        }
    }

    private static IEnumerable<string> SourceFiles(string directory) =>
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Where(path => Path.GetExtension(path) is ".cs" or ".resx");

    private static string RepositoryRoot
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
