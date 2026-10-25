using System.Xml.Linq;
using Alxarafe.Core;
using Alxarafe.Modularity;
using Alxarafe.Resources;
using Xunit;

namespace Alxarafe.ArchitectureTests;

public sealed class ResourceMetadataArchitectureTests
{
    [Fact]
    public void CoreAndModularityNeverReferenceResourcePlatform()
    {
        foreach (var assembly in new[] { typeof(Tenant).Assembly, typeof(ModuleRegistry).Assembly })
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name == typeof(ResourceDefinition).Assembly.GetName().Name);
        Assert.Equal(typeof(ResourceDefinition).Assembly, typeof(ResourceRegistry).Assembly);
        Assert.Equal(typeof(ResourceDefinition).Assembly, typeof(IResourceDefinitionProvider).Assembly);
    }

    [Fact]
    public void ResourcePlatformHasOnlyTheModuleCatalogProjectDependencyAndNoFrameworkPackages()
    {
        string path = Path.Combine(HostBoundaryTests.RepositoryRoot, "src/Alxarafe.Resources/Alxarafe.Resources.csproj");
        var project = XDocument.Load(path);
        Assert.Equal(["../Alxarafe.Modularity/Alxarafe.Modularity.csproj"], project.Descendants("ProjectReference").Select(reference => reference.Attribute("Include")!.Value));
        Assert.Empty(project.Descendants("FrameworkReference"));
        Assert.Empty(project.Descendants("PackageReference"));
        foreach (var reference in typeof(ResourceDefinition).Assembly.GetReferencedAssemblies())
            Assert.True(reference.Name!.StartsWith("System.", StringComparison.Ordinal) || reference.Name == typeof(ModuleRegistry).Assembly.GetName().Name,
                "Unexpected Resource Platform dependency: " + reference.Name);
    }

    [Fact]
    public void SemanticContractsHaveNoClrStorageTypesOrUntypedObjectPayloads()
    {
        foreach (var type in typeof(ResourceDefinition).Assembly.GetExportedTypes())
        {
            foreach (var property in type.GetProperties())
            {
                Assert.NotEqual(typeof(Type), property.PropertyType);
                Assert.NotEqual(typeof(object), property.PropertyType);
                Assert.False(property.PropertyType.IsGenericType && property.PropertyType.GetGenericArguments().Contains(typeof(object)));
            }
        }
        Assert.All(typeof(ResourceDefinition).GetProperties(), property => Assert.Null(property.SetMethod));
        Assert.All(typeof(FieldDefinition).GetProperties(), property => Assert.Null(property.SetMethod));
        Assert.NotEqual(typeof(Tenant).Assembly, typeof(AnnotationVocabulary).Assembly);
    }
}
