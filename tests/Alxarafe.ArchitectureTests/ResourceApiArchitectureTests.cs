using Alxarafe.Core;
using Alxarafe.Resources;
using Alxarafe.Resources.AspNetCore;
using Alxarafe.Resources.Runtime;
using Xunit;

namespace Alxarafe.ArchitectureTests;

public sealed class ResourceApiArchitectureTests
{
    [Fact]
    public void HttpAdapterDependsInwardWithoutBusinessOrPersistenceKnowledge()
    {
        var adapter = typeof(ResourceEndpoints).Assembly;
        foreach (var reference in adapter.GetReferencedAssemblies())
        {
            Assert.DoesNotContain("EntityFrameworkCore", reference.Name!, StringComparison.Ordinal);
            Assert.DoesNotContain("Modules.", reference.Name!, StringComparison.Ordinal);
        }
        Assert.Contains(adapter.GetReferencedAssemblies(), reference => reference.Name == typeof(ResourceRuntime).Assembly.GetName().Name);
        foreach (var assembly in new[] { typeof(Tenant).Assembly, typeof(ResourceDefinition).Assembly, typeof(ResourceRuntime).Assembly })
        {
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name == adapter.GetName().Name);
            if (assembly != typeof(Tenant).Assembly)
                Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name!.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void OpenApiConsumesOnlySemanticRegistryAndPublicApiHasNoTenantSelector()
    {
        Assert.Equal(typeof(ResourceRegistry), Assert.Single(Assert.Single(typeof(ResourceOpenApiTransformer).GetConstructors()).GetParameters()).ParameterType);
        Assert.DoesNotContain(typeof(ResourceEndpoints).GetMethods().SelectMany(method => method.GetParameters()),
            parameter => parameter.Name is "tenantId" or "permissions" or "roles" or "databaseReference");
        Assert.Equal(typeof(System.Text.Json.JsonElement), typeof(ResourceAnnotationDto).GetProperty(nameof(ResourceAnnotationDto.Value))!.PropertyType);
    }
}
