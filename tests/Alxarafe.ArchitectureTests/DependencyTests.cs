using Alxarafe.Modules.Catalog.Application;
using Alxarafe.Modules.Catalog.Domain;
using Alxarafe.Modules.Catalog.Http;
using Alxarafe.Modules.Catalog.ModuleDefinition;
using Xunit;

namespace Alxarafe.ArchitectureTests;

public sealed class DependencyTests
{
    [Fact]
    public void DomainHasNoFrameworkOrPersistenceReferences()
    {
        var names = typeof(Item).Assembly.GetReferencedAssemblies().Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain("Microsoft.AspNetCore.App", names);
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", names);
        Assert.DoesNotContain("Alxarafe.Modularity", names);
        Assert.DoesNotContain(names, x => x?.Contains("Security", StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain(names, x => x?.Contains("Localization", StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain(names, x => x?.Contains("Orchard", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void ApplicationHasNoInfrastructureOrHttpReferences()
    {
        var names = typeof(CreateItemHandler).Assembly.GetReferencedAssemblies().Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain("Microsoft.AspNetCore.App", names);
        Assert.DoesNotContain(names, x => x?.Contains("Identity", StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain(names, x => x?.Contains("Localization", StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain(names, x => x?.Contains("Infrastructure", StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain(names, x => x?.Contains("Orchard", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void CatalogDeclaresItsPermissionsWithoutSecurityInfrastructure()
    {
        var permissions = new CatalogPermissionDefinitionProvider().GetPermissions().Select(permission => permission.Name).ToArray();
        Assert.Contains(CatalogPermissions.ItemsRead, permissions);
        Assert.Contains(CatalogPermissions.ItemsCreate, permissions);
        var names = typeof(CatalogPermissionDefinitionProvider).Assembly.GetReferencedAssemblies().Select(x => x.Name).ToArray();
        Assert.DoesNotContain("Alxarafe.Security.EntityFrameworkCore", names);
    }
}
