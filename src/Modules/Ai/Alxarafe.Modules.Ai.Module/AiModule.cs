using Alxarafe.AspNetCore;
using Alxarafe.Modularity;
using Alxarafe.Modules.Ai.Application;
using Alxarafe.Modules.Ai.Http;
using Alxarafe.Modules.Ai.Infrastructure;
using Alxarafe.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Alxarafe.Modules.Ai;

public sealed class AiModule : IAlxarafeModule, IHttpModule
{
    public const string Id = AiContract.ModuleId;
    public const string DeterministicEnvironment = "Testing";
    private const string HttpClientName = "ai-groq";
    public ModuleDescriptor Descriptor { get; } = new(Id, ModuleApi.CurrentVersion, []);

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddAuthorization(options => options.AddPolicy(AiContract.AssistantPermission, policy =>
            policy.RequireAuthenticatedUser().RequireAssertion(context =>
                context.User.HasClaim(CoreUserClaims.Admin, "true")
                || context.User.HasClaim(PermissionClaim.Type, AiContract.AssistantPermission))));
        services.AddHttpClient(HttpClientName);
        services.AddScoped<IAiModelClient>(CreateModelClient);
        services.AddScoped<AiAssistant>();
    }

    public Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => AiAssistantEndpoints.Map(endpoints);

    public static void UseInvocationAudit(IApplicationBuilder app) =>
        app.UseMiddleware<AiInvocationAuditMiddleware>();

    private static IAiModelClient CreateModelClient(IServiceProvider provider)
    {
        if (provider.GetRequiredService<IHostEnvironment>().IsEnvironment(DeterministicEnvironment))
            return new DeterministicAiModelClient();
        var configuration = provider.GetRequiredService<IConfiguration>();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
        client.Timeout = TimeSpan.FromSeconds(configuration.GetValue(GroqAiModelClient.TimeoutConfiguration, GroqAiModelClient.DefaultTimeoutSeconds));
        return new GroqAiModelClient(client, configuration);
    }
}
