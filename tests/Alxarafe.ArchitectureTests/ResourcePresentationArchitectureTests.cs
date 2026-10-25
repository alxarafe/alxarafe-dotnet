using Alxarafe.Core;
using Alxarafe.Modularity;
using Alxarafe.Resources;
using Alxarafe.Resources.AspNetCore;
using Alxarafe.Resources.Runtime;
using Xunit;

namespace Alxarafe.ArchitectureTests;

public sealed class ResourcePresentationArchitectureTests
{
    [Fact]
    public void LayoutContractsAreImmutableSemanticTypesWithoutFrameworkOrUntypedPayloads()
    {
        Type[] types = [typeof(ResourcePresentationDefinition), typeof(ResourceFormDefinition), typeof(ResourceFormBehavior), typeof(ResourceFormTab), typeof(ResourceFormSection), typeof(ResourceFormFieldGroup)];
        foreach (var type in types)
        {
            Assert.Equal(typeof(ResourceDefinition).Assembly, type.Assembly);
            Assert.All(type.GetProperties(), property => { Assert.Null(property.SetMethod); Assert.NotEqual(typeof(object), property.PropertyType); });
        }
        foreach (var assembly in new[] { typeof(Tenant).Assembly, typeof(ModuleRegistry).Assembly })
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name == typeof(ResourceDefinition).Assembly.GetName().Name);
        Assert.NotEqual(typeof(ResourcePresentationDto).Assembly, typeof(ResourceFormDefinition).Assembly);
        Assert.DoesNotContain(typeof(ResourceFormDefinition).Assembly.GetReferencedAssemblies(), reference => reference.Name!.Contains("AspNetCore", StringComparison.Ordinal) || reference.Name.Contains("EntityFramework", StringComparison.Ordinal) || reference.Name.Contains("Modules.", StringComparison.Ordinal));
    }
    [Fact]
    public void CrudAndActionsDoNotReadOrInterpretLayout()
    {
        string directory = Path.Combine(HostBoundaryTests.RepositoryRoot, "src/Alxarafe.Resources.Runtime");
        foreach (string file in Directory.GetFiles(directory, "*.cs"))
        {
            string source = File.ReadAllText(file);
            Assert.DoesNotContain(".Presentation", source, StringComparison.Ordinal);
            Assert.DoesNotContain(nameof(ResourceFormDefinition), source, StringComparison.Ordinal);
        }
        Assert.Equal(typeof(ResourceRuntime).Assembly, typeof(ResourceActionRuntime).Assembly);
    }
}
