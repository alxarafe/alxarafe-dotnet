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
using Microsoft.Extensions.Logging;

namespace Alxarafe.Modules.Ai;

public sealed class AiModule : IAlxarafeModule, IHttpModule
{
    public const string Id = AiContract.ModuleId;
    public const string DeterministicEnvironment = "Testing";
    private const string HttpClientName = "ai-model";
    public ModuleDescriptor Descriptor { get; } = new(Id, ModuleApi.CurrentVersion, []);

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddAuthorization(options => options.AddPolicy(AiContract.AssistantPermission, policy =>
            policy.RequireAuthenticatedUser().RequireAssertion(context =>
                context.User.HasClaim(CoreUserClaims.Admin, "true")
                || context.User.HasClaim(PermissionClaim.Type, AiContract.AssistantPermission))));
        services.AddSingleton(TimeProvider.System);
        services.AddOptions<DevelopmentUsersOptions>().PostConfigure<IHostEnvironment>((options, environment) =>
        {
            if (!environment.IsDevelopment()) return;
            for (int i = 0; i < options.Users.Count; i++)
            {
                var user = options.Users[i];
                options.Users[i] = user with { Permissions = user.Permissions.Append(AiContract.AssistantPermission).Distinct(StringComparer.Ordinal).ToArray() };
            }
        });
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
        return AiProviderResolver.Resolve(client, configuration, provider.GetRequiredService<ILoggerFactory>());
    }
}
