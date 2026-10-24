using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Alxarafe.Modularity;
using Alxarafe.Modules.Ai;
using Alxarafe.Modules.Ai.Application;
using Alxarafe.Modules.Ai.Http;
using Alxarafe.Modules.Ai.Infrastructure;
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
using Microsoft.Extensions.Logging;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class AiAssistantApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private const string Password = "ai-assistant-fixture-password";
    private const string DirectMessage = "Reply with a short greeting.";
    private const string OneToolMessage = "List the available units of measure.";
    private const string MultipleToolsMessage = "List the available units of measure, storage attributes and item families.";
    private const string UomCapability = "wms.uoms.list";
    private const string StorageAttributeCapability = "wms.storage-attributes.list";
    private const string ItemFamilyCapability = "wms.item-families.list";
    private const string LoginPath = "/api/auth/login";
    private static readonly string[] InvocationFields = ["answer", "invocationId", "toolsUsed"];
    private static readonly string[] ToolFields = ["capabilityId", "invocationId"];
    private static readonly JsonElement EmptyArguments = JsonDocument.Parse("{}").RootElement.Clone();
    private readonly List<Guid> users = [];
    private HttpClient client = null!;
    private string admin = "";
    private string ordinary = "";
    private string aiOnly = "";
    private ModuleStateService states = null!;

    public async Task InitializeAsync()
    {
        client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        states = factory.Services.GetRequiredService<ModuleStateService>();
        await states.EnableAsync(AiModule.Id);
        await states.EnableAsync(WmsModule.Id);
        await using var scope = factory.Services.CreateAsyncScope();
        var administration = scope.ServiceProvider.GetRequiredService<UserAdministration>();
        var identity = scope.ServiceProvider.GetRequiredService<UserManager<AlxarafeUser>>();
        var adminUser = (await administration.CreateAsync("ai-admin-" + Guid.NewGuid().ToString("N") + "@example.test", Password, true))!;
        var ordinaryUser = (await administration.CreateAsync("ai-user-" + Guid.NewGuid().ToString("N") + "@example.test", Password, false))!;
        var aiUser = (await administration.CreateAsync("ai-only-" + Guid.NewGuid().ToString("N") + "@example.test", Password, false))!;
        users.AddRange([adminUser.Id, ordinaryUser.Id, aiUser.Id]);
        var account = (await identity.FindByIdAsync(aiUser.Id.ToString()))!;
        Assert.True((await identity.AddClaimAsync(account,
            new Claim(PermissionClaim.Type, AiContract.AssistantPermission))).Succeeded);
        admin = await LoginAsync(adminUser.Email, Password);
        ordinary = await LoginAsync(ordinaryUser.Email, Password);
        aiOnly = await LoginAsync(aiUser.Email, Password);
    }

    public async Task DisposeAsync()
    {
        await states.EnableAsync(AiModule.Id);
        await states.EnableAsync(WmsModule.Id);
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SecurityDbContext>().Users.Where(user => users.Contains(user.Id)).ExecuteDeleteAsync();
        client.Dispose();
    }

    [Fact]
    public async Task DeterministicInvocationsReturnClosedShapesAndSuccessfulProvenance()
    {
        var aiIds = new HashSet<string>(StringComparer.Ordinal);
        var coreIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (message, expected) in new[]
        {
            (DirectMessage, Array.Empty<string>()),
            (OneToolMessage, new[] { UomCapability }),
            (MultipleToolsMessage, new[] { UomCapability, StorageAttributeCapability, ItemFamilyCapability })
        })
        {
            var response = await Send(message, admin);
            var body = await Body(response, HttpStatusCode.OK);
            Assert.Equal(InvocationFields, body.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
            string invocationId = body.GetProperty("invocationId").GetString()!;
            Assert.False(string.IsNullOrWhiteSpace(invocationId));
            Assert.True(aiIds.Add(invocationId));
            Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("answer").GetString()));
            var tools = body.GetProperty("toolsUsed").EnumerateArray().ToArray();
            Assert.Equal(expected, tools.Select(tool => tool.GetProperty("capabilityId").GetString()!).ToArray());
            foreach (var tool in tools)
            {
                Assert.Equal(ToolFields, tool.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
                string coreId = tool.GetProperty("invocationId").GetString()!;
                Assert.False(string.IsNullOrWhiteSpace(coreId));
                Assert.NotEqual(invocationId, coreId);
                Assert.True(coreIds.Add(coreId));
            }
        }
    }

    [Fact]
    public async Task SemanticToolNamesSelectTheOriginalCapabilityThroughCore()
    {
        const string uomToolName = "wms_uoms_list";
        var expectedNames = new Dictionary<string, string>
        {
            [UomCapability] = uomToolName,
            [StorageAttributeCapability] = "wms_storage-attributes_list",
            [ItemFamilyCapability] = "wms_item-families_list"
        };
        int completions = 0;
        using var host = Host(new StubModelClient(request =>
        {
            completions++;
            foreach (var tool in request.Tools.Where(tool => expectedNames.ContainsKey(tool.CapabilityId)))
                Assert.Equal(expectedNames[tool.CapabilityId], tool.Name);
            if (request.Messages.Any(message => message.Role == AiRoles.Tool))
                return Task.FromResult(new AiModelResponse("Current units were retrieved.", []));
            Assert.Contains(request.Tools, tool => tool.CapabilityId == UomCapability && tool.Name == uomToolName);
            return Task.FromResult(new AiModelResponse(null, [new AiToolRequest("call-uoms", uomToolName, EmptyArguments)]));
        }));
        using var semantic = host.CreateClient();
        var body = await Body(await SendMessage(semantic, OneToolMessage, admin), HttpStatusCode.OK);
        Assert.Equal(UomCapability, Assert.Single(body.GetProperty("toolsUsed").EnumerateArray())
            .GetProperty("capabilityId").GetString());
        Assert.Equal(2, completions);
    }

    [Fact]
    public async Task AuthenticationPermissionModuleAndValidationPrecedenceIsNormative()
    {
        await Error(await Send(DirectMessage, ""), HttpStatusCode.Unauthorized, AiContract.Unauthorized);
        await Error(await SendRaw("{", ""), HttpStatusCode.Unauthorized, AiContract.Unauthorized);
        await Error(await Send(DirectMessage, ordinary), HttpStatusCode.Forbidden, AiContract.Forbidden);
        await Error(await SendRaw("{", ordinary), HttpStatusCode.Forbidden, AiContract.Forbidden);
        await Error(await SendRaw("{", admin), HttpStatusCode.BadRequest, AiContract.InvalidRequest);
        try
        {
            await states.DisableAsync(AiModule.Id);
            await Error(await Send(DirectMessage, admin), HttpStatusCode.ServiceUnavailable, AiContract.ModuleDisabled);
            await Error(await SendRaw("{", admin), HttpStatusCode.ServiceUnavailable, AiContract.ModuleDisabled);
            await Error(await Send(DirectMessage, ""), HttpStatusCode.Unauthorized, AiContract.Unauthorized);
        }
        finally { await states.EnableAsync(AiModule.Id); }
    }

    [Fact]
    public async Task ClosedMessageValidationAcceptsTheScalarLimitAndRejectsEverythingElse()
    {
        string maximum = new('a', AiContract.MessageMaximum);
        string overMaximum = new('a', AiContract.MessageMaximum + 1);
        string maximumScalars = string.Concat(Enumerable.Repeat("📦", AiContract.MessageMaximum));
        foreach (string body in new[]
        {
            "", "{", "[]", "null", "{}", "{\"message\":null}", "{\"message\":\"\"}", "{\"message\":\"  \\t\\r\\n  \"}",
            "{\"message\":42}", "{\"message\":\"ok\",\"history\":[]}", "{\"message\":\"ok\",\"invocationId\":\"caller\"}",
            "{\"message\":\"ok\",\"permissions\":[\"wms.uoms.read\"]}", "{\"message\":\"a\\u0000b\"}",
            "{\"message\":\"\\ud800\"}", "{\"message\":\"" + overMaximum + "\"}"
        })
            await Error(await SendRaw(body, admin), HttpStatusCode.BadRequest, AiContract.InvalidRequest);
        Assert.Equal(HttpStatusCode.OK, (await Send(new string('a', AiContract.MessageMaximum), admin)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(maximumScalars, admin)).StatusCode);
    }

    [Fact]
    public async Task AiOnlyPermissionDoesNotGrantBusinessCapabilityVisibility()
    {
        var response = await Send(MultipleToolsMessage, aiOnly);
        var body = await Body(response, HttpStatusCode.OK);
        Assert.Empty(body.GetProperty("toolsUsed").EnumerateArray());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("answer").GetString()));
        using var wms = new HttpRequestMessage(HttpMethod.Get, "/api/wms/uoms");
        wms.Headers.Authorization = new AuthenticationHeaderValue("Bearer", aiOnly);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(wms)).StatusCode);
    }

    [Fact]
    public async Task DisabledWmsToolsAreHiddenAndRestored()
    {
        try
        {
            await states.DisableAsync(WmsModule.Id);
            var hidden = await Body(await Send(MultipleToolsMessage, admin), HttpStatusCode.OK);
            Assert.Empty(hidden.GetProperty("toolsUsed").EnumerateArray());
        }
        finally { await states.EnableAsync(WmsModule.Id); }
        var restored = await Body(await Send(OneToolMessage, admin), HttpStatusCode.OK);
        Assert.Equal(UomCapability, Assert.Single(restored.GetProperty("toolsUsed").EnumerateArray())
            .GetProperty("capabilityId").GetString());
    }

    [Fact]
    public async Task TemporaryUnavailabilityAndPermanentFailuresStaySafe()
    {
        await AssertSafeFailure(_ => throw new AiModelUnavailableException("provider-secret"),
            HttpStatusCode.ServiceUnavailable, AiContract.AssistantUnavailable);
        await AssertSafeFailure(_ => throw new AiModelFailureException("provider-body-secret"),
            HttpStatusCode.InternalServerError, AiContract.AssistantExecutionFailed);
        await AssertSafeFailure(_ => new AiModelResponse(null,
                [new AiToolRequest("call", "unknown_tool", EmptyArguments)]),
            HttpStatusCode.InternalServerError, AiContract.AssistantExecutionFailed);
    }

    [Fact]
    public async Task CapabilitiesAreReevaluatedImmediatelyBeforeEachCoreInvocation()
    {
        var stub = new StubModelClient(async request =>
        {
            await states.DisableAsync(WmsModule.Id);
            return new AiModelResponse(null, [new AiToolRequest("call", request.Tools[0].Name, EmptyArguments)]);
        });
        try
        {
            using var host = Host(stub);
            using var faulty = host.CreateClient();
            await Error(await SendMessage(faulty, OneToolMessage, admin), HttpStatusCode.InternalServerError,
                AiContract.AssistantExecutionFailed);
        }
        finally { await states.EnableAsync(WmsModule.Id); }
    }

    [Fact]
    public async Task CoreBudgetExhaustionIsASafeExecutionFailure()
    {
        using var host = Host(new StubModelClient(request => Task.FromResult(new AiModelResponse(null,
            [new AiToolRequest(Guid.NewGuid().ToString("N"), request.Tools[0].Name, EmptyArguments)]))));
        using var looping = host.CreateClient();
        await Error(await SendMessage(looping, OneToolMessage, admin), HttpStatusCode.InternalServerError,
            AiContract.AssistantExecutionFailed);
    }

    [Fact]
    public async Task AuditLinksAiSuccessToCoreInvocationsWithoutLoggingPayloads()
    {
        var capture = new CaptureLoggerProvider();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Information);
            logging.AddProvider(capture);
        }));
        using var audit = host.CreateClient();
        string secret = "audit-secret-" + Guid.NewGuid().ToString("N");
        var body = await Body(await SendMessage(audit, OneToolMessage + " " + secret, admin), HttpStatusCode.OK);
        string aiInvocationId = body.GetProperty("invocationId").GetString()!;
        string coreInvocationId = Assert.Single(body.GetProperty("toolsUsed").EnumerateArray())
            .GetProperty("invocationId").GetString()!;
        string[] messages = capture.Messages.ToArray();
        Assert.Contains(messages, message => message.Contains("AI invocation " + aiInvocationId)
            && message.Contains("outcome=completed")
            && message.Contains("tools=" + UomCapability + ":" + coreInvocationId));
        Assert.Contains(messages, message => message.Contains("Capability invocation " + coreInvocationId)
            && message.Contains("capability=" + UomCapability)
            && message.Contains("module=" + WmsModule.Id)
            && message.Contains("outcome=completed"));
        Assert.DoesNotContain(secret, string.Join("\n", messages));
        Assert.DoesNotContain(OneToolMessage, string.Join("\n", messages));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProviderReasoningNeverBecomesPublicContentOrFunctionalAudit(bool hasFinalContent)
    {
        const string reasoning = "private-provider-reasoning";
        const string finalAnswer = "Safe final answer.";
        using var handler = new ReasoningHandler(JsonSerializer.Serialize(new
        {
            choices = new[] { new { finish_reason = "stop", message = new { content = hasFinalContent ? finalAnswer : null, reasoning } } },
            extra_provider_metadata = reasoning
        }));
        using var transport = new HttpClient(handler);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [GroqAiModelClient.ApiKeyConfiguration] = "synthetic-key"
        }).Build();
        var capture = new CaptureLoggerProvider();
        using var host = Host(new GroqAiModelClient(transport, configuration))
            .WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(capture)));
        using var http = host.CreateClient();
        using var response = await SendMessage(http, DirectMessage, admin);
        var body = await Body(response, hasFinalContent ? HttpStatusCode.OK : HttpStatusCode.InternalServerError);
        string json = body.GetRawText();
        Assert.DoesNotContain(reasoning, json);
        Assert.DoesNotContain("reasoning", json);
        if (hasFinalContent)
        {
            Assert.Equal(finalAnswer, body.GetProperty("answer").GetString());
            Assert.Equal(InvocationFields, body.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        }
        Assert.DoesNotContain(capture.Messages, message => message.Contains(reasoning, StringComparison.Ordinal));
    }

    private sealed class ReasoningHandler(string completion) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(completion, Encoding.UTF8, "application/json") });
    }

    private async Task AssertSafeFailure(Func<AiModelRequest, AiModelResponse> response, HttpStatusCode status, string code)
    {
        using var host = Host(new StubModelClient(request => Task.FromResult(response(request))));
        using var faulty = host.CreateClient();
        await Error(await SendMessage(faulty, OneToolMessage, admin), status, code);
    }

    private WebApplicationFactory<Program> Host(IAiModelClient model) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAiModelClient>();
            services.AddScoped(_ => model);
        }));

    private async Task<string> LoginAsync(string email, string password)
    {
        var response = await client.PostAsJsonAsync(LoginPath, new { email, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
    }

    private Task<HttpResponseMessage> Send(string message, string token) => SendMessage(client, message, token);

    private Task<HttpResponseMessage> SendRaw(string body, string token) => Send(client, body, token);

    private static Task<HttpResponseMessage> SendMessage(HttpClient http, string message, string token) =>
        Send(http, JsonSerializer.Serialize(new { message }), token);

    private static async Task<HttpResponseMessage> Send(HttpClient http, string body, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, AiAssistantEndpoints.Path)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        if (token != "") request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await http.SendAsync(request);
    }

    private static async Task<JsonElement> Body(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.True(status == response.StatusCode, await response.Content.ReadAsStringAsync());
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        if (status == HttpStatusCode.Unauthorized) Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
        else Assert.Empty(response.Headers.WwwAuthenticate);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task Error(HttpResponseMessage response, HttpStatusCode status, string code) =>
        ContractErrors.Single(await Body(response, status), code);

    private sealed class StubModelClient(Func<AiModelRequest, Task<AiModelResponse>> complete) : IAiModelClient
    {
        public Task<AiModelResponse> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken = default) =>
            complete(request);
    }

    private sealed class CaptureLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CaptureLogger(categoryName, Messages);

        public void Dispose() { }

        private sealed class CaptureLogger(string category, ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (category.EndsWith(nameof(AiInvocationAuditMiddleware), StringComparison.Ordinal)
                    || category.EndsWith(nameof(CapabilityInvocationAuditMiddleware), StringComparison.Ordinal))
                    messages.Enqueue(formatter(state, exception));
            }
        }
    }
}
