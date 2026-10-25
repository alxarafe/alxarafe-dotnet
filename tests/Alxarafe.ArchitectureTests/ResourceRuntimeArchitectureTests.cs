using Alxarafe.Core;
using Alxarafe.Modularity;
using Alxarafe.Resources;
using Alxarafe.Resources.Runtime;
using Xunit;

namespace Alxarafe.ArchitectureTests;

public sealed class ResourceRuntimeArchitectureTests
{
    [Fact]
    public void RuntimeDependsOnlyInwardOnSemanticCoreAndModularityContracts()
    {
        string[] allowed = [typeof(Tenant).Assembly.GetName().Name!, typeof(ResourceDefinition).Assembly.GetName().Name!, typeof(ModuleRegistry).Assembly.GetName().Name!];
        foreach (var reference in typeof(ResourceRuntime).Assembly.GetReferencedAssemblies())
            Assert.True(reference.Name!.StartsWith("System.", StringComparison.Ordinal) || allowed.Contains(reference.Name, StringComparer.Ordinal), "Unexpected runtime dependency: " + reference.Name);
        foreach (var assembly in new[] { typeof(Tenant).Assembly, typeof(ModuleRegistry).Assembly, typeof(ResourceDefinition).Assembly })
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name == typeof(ResourceRuntime).Assembly.GetName().Name);
    }

    [Fact]
    public void PublicCrudAcceptsOnlyBusinessInputsNeverSecurityOrPersistenceSelectors()
    {
        Type[] allowed = [typeof(string), typeof(ResourceRecord), typeof(ResourceIdentifier), typeof(ResourceQuery), typeof(CancellationToken)];
        foreach (var method in typeof(ResourceRuntime).GetMethods().Where(method => method.DeclaringType == typeof(ResourceRuntime)))
            foreach (var parameter in method.GetParameters()) Assert.Contains(parameter.ParameterType, allowed);
        Assert.Equal(typeof(IReadOnlyDictionary<string, ScalarValue>), typeof(ResourceRecord).GetProperty(nameof(ResourceRecord.Values))!.PropertyType);
        Assert.All(typeof(ResourceRecord).GetProperties(), property => Assert.Null(property.SetMethod));
        Assert.DoesNotContain(typeof(IResourceRepository).GetMethods().SelectMany(method => method.GetParameters()),
            parameter => parameter.Name is "tenantId" or "permissions" or "moduleEnabled" or "databaseReference");
    }
}
