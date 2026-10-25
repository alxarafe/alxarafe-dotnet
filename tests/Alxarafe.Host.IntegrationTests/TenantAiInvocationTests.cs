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

public sealed class TenantAiInvocationTests(TenantApplicationFactory factory) : IClassFixture<TenantApplicationFactory>
{
    private const string Password = "tenant-ai-fixture-password";
    private const string SecondDatabaseReference = "test_b";
    private static readonly TimeSpan ConcurrentWait = TimeSpan.FromSeconds(10);
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
        string tenantId = DevelopmentTenantBootstrap.TenantId;
        string legacyKey = "invocation-legacy-" + Guid.NewGuid().ToString("N");
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
            TenantId = legacyKey,
            CanonicalTenantId = tenantId,
            ProviderOverride = OllamaAiModelClient.ProviderName,
            ModelOverride = EffectiveAiConfigurationTests.LocalModel
        });
        await platform.SaveChangesAsync();
        await platform.TenantMemberships.Where(row => row.UserId == user.Id).ExecuteDeleteAsync();
        try
        {
            using var login = await bootstrap.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = Password });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            string token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
            using var globalWire = new EffectiveAiConfigurationTests.Wire();
            using var tenantWire = new EffectiveAiConfigurationTests.Wire();
            using var globalHost = Host(globalWire);
            using var tenantHost = Host(tenantWire);
            using var globalClient = globalHost.CreateClient(new() { AllowAutoRedirect = false });
            using var tenantClient = tenantHost.CreateClient(new() { AllowAutoRedirect = false });
            var globalResponse = await Ask(globalClient, token, tenantId);
            platform.TenantMemberships.Add(new TenantMembershipRow { UserId = user.Id, TenantId = tenantId, Active = true });
            await platform.SaveChangesAsync();
            var tenantResponse = await Ask(tenantClient, token, "untrusted-other-tenant");
            var responses = new[] { globalResponse, tenantResponse };
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
                var invocation = await usage.Invocations.SingleAsync(row => row.InvocationId == id);
                Assert.Equal(user.Id, invocation.UserId);
                Assert.Equal(i == 0 ? null : tenantId, invocation.TenantId);
            }

            await using var scope = factory.Services.CreateAsyncScope();
            Assert.Null(scope.ServiceProvider.GetRequiredService<ICurrentTenantContext>().TenantId);
        }
        finally
        {
            await using var usage = await factory.Services.GetRequiredService<IDbContextFactory<AiUsageDbContext>>().CreateDbContextAsync();
            await usage.UsageRecords.Where(row => row.InvocationId != null && invocationIds.Contains(row.InvocationId)).ExecuteDeleteAsync();
            await usage.Invocations.Where(row => invocationIds.Contains(row.InvocationId)).ExecuteDeleteAsync();
            await platform.TenantAiSettings.Where(row => row.TenantId == legacyKey).ExecuteDeleteAsync();
            await platform.TenantMemberships.Where(row => row.UserId == user.Id).ExecuteDeleteAsync();
            await setup.ServiceProvider.GetRequiredService<SecurityDbContext>().Users.Where(row => row.Id == user.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task AuthoritativeConcurrentRequestsRefreshSettingsAndAttributeSuccessAndFailure()
    {
        using var bootstrap = factory.CreateClient();
        await factory.Services.GetRequiredService<ModuleStateService>().EnableAsync(AiModule.Id);
        await using var setup = factory.Services.CreateAsyncScope();
        var users = setup.ServiceProvider.GetRequiredService<UserAdministration>();
        await using var platform = await factory.Services.GetRequiredService<IDbContextFactory<PlatformDbContext>>().CreateDbContextAsync();
        string tenantA = DevelopmentTenantBootstrap.TenantId, tenantB = "ai-b-" + Guid.NewGuid().ToString("N");
        var userIds = new List<Guid>();
        const string changedModel = "updated-model";
        try
        {
            string a = await Arrange(tenantA, GroqAiModelClient.ProviderName, EffectiveAiConfigurationTests.GlobalModel);
            string b = await Arrange(tenantB, OllamaAiModelClient.ProviderName, EffectiveAiConfigurationTests.LocalModel);
            using var wire = new EffectiveAiConfigurationTests.Wire();
            var bothEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int entered = 0;
            wire.BeforeResponse = async () =>
            {
                if (Interlocked.Increment(ref entered) == 2) bothEntered.SetResult();
                await bothEntered.Task.WaitAsync(ConcurrentWait);
            };
            using var host = Host(wire);
            using var client = host.CreateClient(new() { AllowAutoRedirect = false });
            var responses = await Task.WhenAll(Ask(client, a, tenantB), Ask(client, b, tenantA));
            wire.BeforeResponse = null;
            await using var usage = await factory.Services.GetRequiredService<IDbContextFactory<AiUsageDbContext>>().CreateDbContextAsync();
            await AssertInvocation(responses[0], userIds[0], tenantA, GroqAiModelClient.ProviderName, EffectiveAiConfigurationTests.GlobalModel);
            await AssertInvocation(responses[1], userIds[1], tenantB, OllamaAiModelClient.ProviderName, EffectiveAiConfigurationTests.LocalModel);
            Assert.Equal(2, wire.Calls.Count);
            var settings = await platform.TenantAiSettings.SingleAsync(row => row.CanonicalTenantId == tenantA);
            settings.ModelOverride = changedModel;
            await platform.SaveChangesAsync();
            await AssertInvocation(await Ask(client, a, tenantB), userIds[0], tenantA, GroqAiModelClient.ProviderName, changedModel);
            wire.Fail = true;
            using var failure = new HttpRequestMessage(HttpMethod.Post, InvokePath) { Content = JsonContent.Create(new { message = "Reply briefly." }) };
            failure.Headers.Authorization = new AuthenticationHeaderValue("Bearer", b);
            using var failed = await client.SendAsync(failure);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
            var failedInvocation = await usage.Invocations.AsNoTracking().SingleAsync(row => row.UserId == userIds[1] && row.Outcome == AiInvocationOutcome.Failed);
            Assert.Equal(tenantB, failedInvocation.TenantId);
            var failedAttempt = await usage.UsageRecords.SingleAsync(row => row.InvocationId == failedInvocation.InvocationId);
            Assert.Equal(OllamaAiModelClient.ProviderName, failedAttempt.Provider);
            Assert.Equal(EffectiveAiConfigurationTests.LocalModel, failedAttempt.Model);
            Assert.Equal(AiExecutionOutcome.Failed, failedAttempt.Outcome);
            Assert.Null(failedAttempt.ProviderCost);
            Assert.Null(failedAttempt.ProviderCostCurrency);
            Assert.Null(failedAttempt.BillingMode);

            async Task AssertInvocation(JsonElement response, Guid userId, string tenantId, string provider, string model)
            {
                string id = response.GetProperty("invocationId").GetString()!;
                var invocation = await usage.Invocations.AsNoTracking().SingleAsync(row => row.InvocationId == id);
                Assert.Equal(tenantId, invocation.TenantId);
                Assert.Equal(userId, invocation.UserId);
                Assert.Equal(AiInvocationOutcome.Completed, invocation.Outcome);
                Assert.Equal(1, invocation.ModelCalls);
                var attempt = await usage.UsageRecords.SingleAsync(row => row.InvocationId == id);
                Assert.Equal(provider, attempt.Provider);
                Assert.Equal(model, attempt.Model);
                Assert.Equal(EffectiveAiConfigurationTests.ReportedModel, attempt.ReportedModel);
                Assert.Equal(3, attempt.InputTokens);
                Assert.Equal(2, attempt.OutputTokens);
                Assert.True(attempt.Duration >= TimeSpan.Zero);
                Assert.Null(attempt.ProviderCost);
                Assert.Null(attempt.BillingMode);
            }
        }
        finally
        {
            await using var usage = await factory.Services.GetRequiredService<IDbContextFactory<AiUsageDbContext>>().CreateDbContextAsync();
            var ids = await usage.Invocations.Where(row => row.UserId != null && userIds.Contains(row.UserId.Value)).Select(row => row.InvocationId).ToArrayAsync();
            await usage.UsageRecords.Where(row => row.InvocationId != null && ids.Contains(row.InvocationId)).ExecuteDeleteAsync();
            await usage.Invocations.Where(row => ids.Contains(row.InvocationId)).ExecuteDeleteAsync();
            await platform.TenantMemberships.Where(row => userIds.Contains(row.UserId)).ExecuteDeleteAsync();
            await platform.TenantAiSettings.Where(row => row.CanonicalTenantId == tenantA || row.CanonicalTenantId == tenantB).ExecuteDeleteAsync();
            await platform.Tenants.Where(row => row.Id == tenantB).ExecuteDeleteAsync();
            await setup.ServiceProvider.GetRequiredService<SecurityDbContext>().Users.Where(row => userIds.Contains(row.Id)).ExecuteDeleteAsync();
        }

        async Task<string> Arrange(string tenantId, string provider, string model)
        {
            // Email deliberately names the other tenant; only membership is authoritative.
            string otherTenant = tenantId == tenantA ? tenantB : tenantA;
            var user = (await users.CreateAsync(otherTenant + "@example.test", Password, false))!;
            userIds.Add(user.Id);
            var identity = setup.ServiceProvider.GetRequiredService<UserManager<AlxarafeUser>>();
            var account = (await identity.FindByIdAsync(user.Id.ToString()))!;
            Assert.True((await identity.AddClaimAsync(account, new Claim(PermissionClaim.Type, AiContract.AssistantPermission))).Succeeded);
            await platform.TenantMemberships.Where(row => row.UserId == user.Id).ExecuteDeleteAsync();
            if (tenantId == tenantB)
                platform.Tenants.Add(new TenantRow { Id = tenantId, Key = tenantId, DisplayName = "Synthetic AI tenant", DatabaseReference = SecondDatabaseReference, Active = true });
            platform.TenantMemberships.Add(new TenantMembershipRow { UserId = user.Id, TenantId = tenantId, Active = true });
            // Deliberately different legacy key: runtime must follow only the canonical link.
            platform.TenantAiSettings.Add(new TenantAiSettingsRow { TenantId = "legacy-" + tenantId, CanonicalTenantId = tenantId, ProviderOverride = provider, ModelOverride = model });
            await platform.SaveChangesAsync();
            using var login = await bootstrap.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = Password });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            return (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        }
    }

    private WebApplicationFactory<Program> Host(EffectiveAiConfigurationTests.Wire wire) =>
        factory.WithWebHostBuilder(builder =>
        {
            // Exercise the real production DI path, but replace HTTP transport with a synthetic handler.
            builder.UseEnvironment(Environments.Production);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddConfiguration(EffectiveAiConfigurationTests.Configuration()));
            builder.ConfigureServices(services =>
            {
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
