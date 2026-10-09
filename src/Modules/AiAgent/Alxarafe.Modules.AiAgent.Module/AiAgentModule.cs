using Alxarafe.AspNetCore;
using Alxarafe.Modularity;
using Alxarafe.Modules.AiAgent.Application;
using Alxarafe.Modules.AiAgent.Http;
using Alxarafe.Modules.AiAgent.Infrastructure;
using Alxarafe.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Alxarafe.Modules.AiAgent.ModuleDefinition;

public sealed class AiAgentModule : IAlxarafeModule, IHttpModule
{
    public string Id => "AiAgent";
    public IReadOnlyCollection<string> Dependencies => [];

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<AiAgentDbContext>((provider, options) =>
        {
            var connectionString = provider.GetRequiredService<IConfiguration>().GetConnectionString("AiAgent");
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("The AiAgent connection string must be configured.");
            if (provider.GetRequiredService<IHostEnvironment>().IsEnvironment("Testing") &&
                !string.Equals(new NpgsqlConnectionStringBuilder(connectionString).Database, "alxarafe_ai_test", StringComparison.Ordinal))
                throw new InvalidOperationException("Testing requires the AiAgent connection to use alxarafe_ai_test.");
            options.UseNpgsql(connectionString);
        });
        services.AddScoped<IKnowledgeRepository, EfKnowledgeRepository>();
        services.AddScoped<CreateKnowledgeEntryHandler>();
        services.AddScoped<GetKnowledgeEntryByIdHandler>();
        services.AddSingleton<IPermissionDefinitionProvider, AiAgentPermissionDefinitionProvider>();
        services.AddOptions<DevelopmentUsersOptions>().Configure<IConfiguration>((options, configuration) =>
        {
            options.Users.Add(new DevelopmentUser("ai-reader@example.test",
                configuration["AiAgentSeed:ReaderPassword"] ?? "Reader_dev_only_123!", [AiAgentPermissions.KnowledgeRead]));
            options.Users.Add(new DevelopmentUser("ai-writer@example.test",
                configuration["AiAgentSeed:WriterPassword"] ?? "Creator_dev_only_123!", [AiAgentPermissions.KnowledgeRead, AiAgentPermissions.KnowledgeWrite]));
        });
    }

    public async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AiAgentDbContext>().Database.MigrateAsync(cancellationToken);
    }

    public void MapEndpoints(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints) => AiAgentEndpoints.Map(endpoints);
}

public sealed class AiAgentPermissionDefinitionProvider : IPermissionDefinitionProvider
{
    public IEnumerable<PermissionDefinition> GetPermissions() =>
    [
        new(AiAgentPermissions.KnowledgeRead, "Read knowledge entries"),
        new(AiAgentPermissions.KnowledgeWrite, "Write knowledge entries")
    ];
}
