using System.Xml.Linq;
using Alxarafe.Modularity;
using Alxarafe.Modules.AiAgent.Application;
using Alxarafe.Modules.AiAgent.Domain;
using Alxarafe.Modules.AiAgent.Http;
using Alxarafe.Modules.AiAgent.Infrastructure;
using Alxarafe.Modules.AiAgent.ModuleDefinition;
using Alxarafe.Modules.Catalog.ModuleDefinition;
using Alxarafe.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alxarafe.ArchitectureTests;

public sealed class AiAgentDependencyTests
{
    [Fact]
    public void DomainAndApplicationHaveNoTransportOrPersistenceDependencies()
    {
        foreach (var assembly in new[] { typeof(KnowledgeEntry).Assembly, typeof(CreateKnowledgeEntryHandler).Assembly })
        {
            var names = assembly.GetReferencedAssemblies().Select(reference => reference.Name);
            string[] forbidden = ["Microsoft.AspNetCore", "Microsoft.EntityFrameworkCore", "Npgsql", ".Infrastructure", ".Http", "Security", "Localization"];
            foreach (var pattern in forbidden)
                Assert.DoesNotContain(names, name => name?.Contains(pattern, StringComparison.Ordinal) == true);
        }
    }

    [Fact]
    public void ModuleDeclaresItsOwnPermissionsWithoutIdentityPersistence()
    {
        var permissions = new AiAgentPermissionDefinitionProvider().GetPermissions().Select(permission => permission.Name);
        Assert.Equal([AiAgentPermissions.KnowledgeRead, AiAgentPermissions.KnowledgeWrite], permissions);
        Assert.DoesNotContain(typeof(AiAgentModule).Assembly.GetReferencedAssemblies(),
            reference => reference.Name == "Alxarafe.Security.EntityFrameworkCore");
    }

    [Fact]
    public void SiblingModuleAssembliesHaveNoReferencesToEachOther()
    {
        Type[] aiTypes = [typeof(KnowledgeEntry), typeof(CreateKnowledgeEntryHandler), typeof(AiAgentDbContext), typeof(AiAgentEndpoints), typeof(AiAgentModule)];
        Type[] catalogTypes = [typeof(Alxarafe.Modules.Catalog.Domain.Item), typeof(Alxarafe.Modules.Catalog.Application.CreateItemHandler),
            typeof(Alxarafe.Modules.Catalog.Infrastructure.CatalogDbContext), typeof(Alxarafe.Modules.Catalog.Http.CatalogEndpoints), typeof(CatalogModule)];
        foreach (var type in aiTypes)
            Assert.DoesNotContain(type.Assembly.GetReferencedAssemblies(), reference => reference.Name?.StartsWith("Alxarafe.Modules.Catalog.", StringComparison.Ordinal) == true);
        foreach (var type in catalogTypes)
            Assert.DoesNotContain(type.Assembly.GetReferencedAssemblies(), reference => reference.Name?.StartsWith("Alxarafe.Modules.AiAgent.", StringComparison.Ordinal) == true);
    }

    [Theory]
    [InlineData("AiAgent")]
    [InlineData("Catalog")]
    public void ModuleCanComposeAloneWithoutItsSibling(string id)
    {
        var assembly = id == "AiAgent" ? typeof(AiAgentModule).Assembly : typeof(CatalogModule).Assembly;
        var services = new ServiceCollection();
        services.AddAlxarafeModules(assembly);
        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<ModuleRuntime>();
        Assert.Equal(id, Assert.Single(runtime.Modules).Id);
        Assert.Empty(runtime.Modules[0].Dependencies);
        var permissions = provider.GetServices<IPermissionDefinitionProvider>().SelectMany(definition => definition.GetPermissions());
        var prefix = id == "AiAgent" ? "ai." : "catalog.";
        Assert.All(permissions, permission => Assert.StartsWith(prefix, permission.Name, StringComparison.Ordinal));
    }

    [Fact]
    public void SiblingProjectsHaveNoReferencesToEachOtherEvenWhenUnused()
    {
        foreach (var module in new[] { "AiAgent", "Catalog" })
        {
            var sibling = module == "AiAgent" ? "Catalog" : "AiAgent";
            var directory = Path.Combine(HostBoundaryTests.RepositoryRoot, "src/Modules", module);
            foreach (var file in Directory.EnumerateFiles(directory, "*.csproj", SearchOption.AllDirectories))
            {
                var references = XDocument.Load(file).Descendants("ProjectReference")
                    .Select(reference => reference.Attribute("Include")!.Value);
                Assert.DoesNotContain(references, reference => reference.Contains($"Alxarafe.Modules.{sibling}.", StringComparison.Ordinal));
            }
        }
    }
}
