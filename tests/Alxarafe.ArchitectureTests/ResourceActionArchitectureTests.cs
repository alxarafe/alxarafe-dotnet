using Alxarafe.Core;
using Alxarafe.Resources;
using Alxarafe.Resources.Runtime;
using Xunit;

namespace Alxarafe.ArchitectureTests;

public sealed class ResourceActionArchitectureTests
{
    [Fact]
    public void ActionContractsAreNeutralTypedAndNeverAcceptSecurityAuthority()
    {
        Assert.Equal(typeof(IReadOnlyDictionary<string, ScalarValue>), typeof(ResourceActionInput).GetProperty(nameof(ResourceActionInput.Values))!.PropertyType);
        Type[] allowed = [typeof(string), typeof(ResourceActionTarget), typeof(ResourceActionInput), typeof(CancellationToken)];
        foreach (var method in typeof(ResourceActionRuntime).GetMethods().Where(method => method.DeclaringType == typeof(ResourceActionRuntime)))
            foreach (var parameter in method.GetParameters()) Assert.Contains(parameter.ParameterType, allowed);
        foreach (var type in new[] { typeof(IResourceActionHandler), typeof(IResourceActionAvailabilityProvider) })
            Assert.DoesNotContain(type.GetMethods().SelectMany(method => method.GetParameters()), parameter => parameter.ParameterType == typeof(object) || parameter.ParameterType.Namespace?.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) == true);
        foreach (var type in new[] { typeof(ResourceActionExecutionContext), typeof(ResourceActionContext) })
            Assert.DoesNotContain(type.GetConstructors().SelectMany(constructor => constructor.GetParameters()), parameter => parameter.ParameterType == typeof(object) || parameter.ParameterType.Namespace?.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) == true);
        foreach (var assembly in new[] { typeof(ResourceDefinition).Assembly, typeof(ResourceActionRuntime).Assembly, typeof(Tenant).Assembly })
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name!.Contains("Modules.", StringComparison.Ordinal) || reference.Name.Contains("EntityFramework", StringComparison.Ordinal) || reference.Name.Contains("AspNetCore", StringComparison.Ordinal));
    }
}
