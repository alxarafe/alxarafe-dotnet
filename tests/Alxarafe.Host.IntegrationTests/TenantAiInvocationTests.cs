using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Alxarafe.Core;
using Alxarafe.EntityFrameworkCore;
using Alxarafe.Modularity;
using Alxarafe.Modules.Ai;
using Alxarafe.Modules.Ai.Application;
using Alxarafe.Modules.Ai.Infrastructure;
using Alxarafe.Modules.Ai.Persistence;
using Alxarafe.Modules.Wms;
using Alxarafe.Security;
using Alxarafe.Security.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class TenantAiInvocationTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Password = "tenant-ai-fixture-password";
    private const string InvokePath = "/api/ai/assistant/invocations";
    private static readonly string[] PublicFields = ["answer", "invocationId", "toolsUsed"];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EffectiveSelectionPersistsUsageWithoutChangingAuthorizedToolsOrPublicContract(bool administrator)
    {
        using var bootstrap = factory.CreateClient();
        await factory.Services.GetRequiredService<ModuleStateService>().EnableAsync(AiModule.Id);
        await factory.Services.GetRequiredService<ModuleStateService>().EnableAsync(WmsModule.Id);
        string tenantId = "invocation-tenant-" + Guid.NewGuid().ToString("N");
        var invocationIds = new List<string>();
        await using var setup = factory.Services.CreateAsyncScope();
        var user = (await setup.ServiceProvider.GetRequiredService<UserAdministration>().CreateAsync(
            "tenant-ai-" + Guid.NewGuid().ToString("N") + "@example.test", Password, administrator))!;
        var identity = setup.ServiceProvider.GetRequiredService<UserManager<AlxarafeUser>>();
        if (!administrator)
        {
            var account = (await identity.FindByIdAsync(user.Id.ToString()))!;
            Assert.True((await identity.AddClaimAsync(account, new Claim(PermissionClaim.Type, AiContract.AssistantPermission))).Succeeded);
        }
        await using var platform = await factory.Services.GetRequiredService<IDbContextFactory<PlatformDbContext>>().CreateDbContextAsync();
        platform.TenantAiSettings.Add(new TenantAiSettingsRow
        {
            TenantId = tenantId,
            ProviderOverride = OllamaAiModelClient.ProviderName,
            ModelOverride = EffectiveAiConfigurationTests.LocalModel
        });
        await platform.SaveChangesAsync();
        try
        {
            using var login = await bootstrap.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = Password });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            string token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
            using var globalWire = new EffectiveAiConfigurationTests.Wire();
            using var tenantWire = new EffectiveAiConfigurationTests.Wire();
            using var globalHost = Host(null, globalWire);
            using var tenantHost = Host(tenantId, tenantWire);
            using var globalClient = globalHost.CreateClient(new() { AllowAutoRedirect = false });
            using var tenantClient = tenantHost.CreateClient(new() { AllowAutoRedirect = false });
            var responses = await Task.WhenAll(Ask(globalClient, token, tenantId), Ask(tenantClient, token, tenantId));
            foreach (var response in responses)
            {
                invocationIds.Add(response.GetProperty("invocationId").GetString()!);
                Assert.Equal(PublicFields, response.EnumerateObject().Select(property => property.Name).Order());
                Assert.Empty(response.GetProperty("toolsUsed").EnumerateArray());
            }
            var globalCall = Assert.Single(globalWire.Calls);
            var tenantCall = Assert.Single(tenantWire.Calls);
            Assert.Equal(EffectiveAiConfigurationTests.GlobalModel, globalCall.Model);
            Assert.Equal(EffectiveAiConfigurationTests.LocalModel, tenantCall.Model);
            Assert.Equal(ToolNames(globalCall), ToolNames(tenantCall));
            Assert.Equal(administrator, globalCall.Tools.Length > 0);
            using var wms = new HttpRequestMessage(HttpMethod.Get, "/api/wms/uoms");
            wms.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var wmsResponse = await tenantClient.SendAsync(wms);
            Assert.Equal(administrator ? HttpStatusCode.OK : HttpStatusCode.Forbidden, wmsResponse.StatusCode);
            await using var usage = await factory.Services.GetRequiredService<IDbContextFactory<AiUsageDbContext>>().CreateDbContextAsync();
            for (int i = 0; i < invocationIds.Count; i++)
            {
                string id = invocationIds[i];
                var record = await usage.UsageRecords.SingleAsync(row => row.InvocationId == id);
                Assert.Equal(i == 0 ? GroqAiModelClient.ProviderName : OllamaAiModelClient.ProviderName, record.Provider);
                Assert.Equal(i == 0 ? EffectiveAiConfigurationTests.GlobalModel : EffectiveAiConfigurationTests.LocalModel, record.Model);
                Assert.Equal(EffectiveAiConfigurationTests.ReportedModel, record.ReportedModel);
                Assert.Null(record.BillingMode);
                Assert.Null(record.ProviderCost);
                Assert.Equal(user.Id, (await usage.Invocations.SingleAsync(row => row.InvocationId == id)).UserId);
            }
            // Attribution is deliberately deferred until there is a runtime tenant authority.
            Assert.Null(usage.Model.FindEntityType(typeof(AiInvocationUsage))!.FindProperty("TenantId"));
            await using var scope = factory.Services.CreateAsyncScope();
            Assert.Null(scope.ServiceProvider.GetRequiredService<ICurrentTenantContext>().TenantId);
        }
        finally
        {
            await using var usage = await factory.Services.GetRequiredService<IDbContextFactory<AiUsageDbContext>>().CreateDbContextAsync();
            await usage.UsageRecords.Where(row => row.InvocationId != null && invocationIds.Contains(row.InvocationId)).ExecuteDeleteAsync();
            await usage.Invocations.Where(row => invocationIds.Contains(row.InvocationId)).ExecuteDeleteAsync();
            await platform.TenantAiSettings.Where(row => row.TenantId == tenantId).ExecuteDeleteAsync();
            await setup.ServiceProvider.GetRequiredService<SecurityDbContext>().Users.Where(row => row.Id == user.Id).ExecuteDeleteAsync();
        }
    }

    private WebApplicationFactory<Program> Host(string? tenantId, EffectiveAiConfigurationTests.Wire wire) =>
        factory.WithWebHostBuilder(builder =>
        {
            // Exercise the real production DI path, but replace HTTP transport with a synthetic handler.
            builder.UseEnvironment(Environments.Production);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddConfiguration(EffectiveAiConfigurationTests.Configuration()));
            builder.ConfigureServices(services =>
            {
                if (tenantId is not null)
                {
                    services.RemoveAll<ICurrentTenantContext>();
                    services.AddScoped<ICurrentTenantContext>(_ => new EffectiveAiConfigurationTests.Tenant(tenantId));
                }
                services.AddHttpClient(AiModule.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => wire);
            });
        });

    private static string[] ToolNames(EffectiveAiConfigurationTests.Wire.Call call) => call.Tools
        .Select(tool => tool.GetProperty("function").GetProperty("name").GetString()!).Order(StringComparer.Ordinal).ToArray();

    private static async Task<JsonElement> Ask(HttpClient client, string token, string spoofedTenant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, InvokePath)
        {
            Content = JsonContent.Create(new { message = "Reply briefly." })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        // An arbitrary header must not establish runtime tenant identity.
        request.Headers.Add("X-Tenant-Id", spoofedTenant);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
