using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using Alxarafe.Modularity;
using Alxarafe.Modules.Wms;
using Alxarafe.Modules.Wms.Application;
using Alxarafe.Modules.Wms.Http;
using Alxarafe.Modules.Wms.Infrastructure;
using Alxarafe.Security.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class CapabilityInvocationApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private const string Password = "capability-invocation-password";
    private const string UomCapability = "wms.uoms.list";
    private const string StorageAttributeCapability = "wms.storage-attributes.list";
    private const string ItemFamilyCapability = "wms.item-families.list";
    private const string BearerScheme = "Bearer";
    private const string EmptyInput = "{\"input\":{}}";
    private const int LabelMaximum = 255;
    private static readonly string[] InvocationFields = ["capabilityId", "invocationId", "output"];
    private static readonly string[] CollectionFields = ["items", "limit", "offset", "order", "total"];
    private static readonly string[] InvalidBodies =
    [
        "", "{", "[]", "true", "42", "\"scalar\"", "null", "{}", "{\"input\":null}", "{\"input\":[]}",
        "{\"input\":{},\"extra\":true}", "{\"input\":{},\"input\":{}}", "{\"input\":{\"extra\":1}}",
        "{\"input\":{\"offset\":-1}}", "{\"input\":{\"offset\":\"0\"}}", "{\"input\":{\"offset\":0.5}}",
        "{\"input\":{\"offset\":1e2}}", "{\"input\":{\"offset\":null}}", "{\"input\":{\"limit\":0}}",
        "{\"input\":{\"limit\":101}}", "{\"input\":{\"limit\":1.0}}", "{\"input\":{\"limit\":true}}",
        "{\"input\":{\"limit\":null}}", "{\"input\":{\"offset\":0,\"offset\":1}}",
        "{\"input\":{\"limit\":1,\"limit\":1}}", "{\"Input\":{}}"
    ];
    private readonly List<Guid> users = [];
    private readonly string run = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    private HttpClient client = null!;
    private string admin = "";
    private string ordinary = "";
    private Guid adminId;
    private string uomCode = "";
    private string storageAttributeCode = "";
    private string itemFamilyCode = "";
    private string maximumLabelUomCode = "";
    private ModuleStateService states = null!;
    private UomCatalogue uoms = null!;
    private StorageAttributeCatalogue storageAttributes = null!;
    private ItemFamilyCatalogue itemFamilies = null!;
    private IDbContextFactory<WmsDbContext> contexts = null!;

    public async Task InitializeAsync()
    {
        client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        states = factory.Services.GetRequiredService<ModuleStateService>();
        contexts = factory.Services.GetRequiredService<IDbContextFactory<WmsDbContext>>();
        uoms = factory.Services.GetRequiredService<UomCatalogue>();
        storageAttributes = factory.Services.GetRequiredService<StorageAttributeCatalogue>();
        itemFamilies = factory.Services.GetRequiredService<ItemFamilyCatalogue>();
        await states.EnableAsync(WmsModule.Id);
        await using var scope = factory.Services.CreateAsyncScope();
        var administration = scope.ServiceProvider.GetRequiredService<UserAdministration>();
        for (int index = 0; index < 2; index++)
        {
            bool isAdmin = index == 0;
            var user = (await administration.CreateAsync("invocation-" + Guid.NewGuid().ToString("N") + "@example.test", Password, isAdmin))!;
            users.Add(user.Id);
            var login = await client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = Password });
            string token = (await Body(login)).GetProperty("accessToken").GetString()!;
            if (isAdmin) { admin = token; adminId = user.Id; }
            else ordinary = token;
        }
        uomCode = "A0" + run;
        storageAttributeCode = "A0" + run;
        itemFamilyCode = "A0" + run;
        maximumLabelUomCode = "ZZ" + run;
        string maximumLabel = string.Concat(Enumerable.Repeat("📦", LabelMaximum));
        Assert.Equal(CreateUomOutcome.Created, (await uoms.CreateAsync(uomCode, "Invocation UOM")).Outcome);
        Assert.Equal(CreateUomOutcome.Created, (await uoms.CreateAsync(maximumLabelUomCode, maximumLabel)).Outcome);
        Assert.Equal(CreateStorageAttributeOutcome.Created, (await storageAttributes.CreateAsync(storageAttributeCode, "Invocation attribute")).Outcome);
        Assert.Equal(CreateItemFamilyOutcome.Created, (await itemFamilies.CreateAsync(itemFamilyCode, "Invocation family")).Outcome);
    }

    public async Task DisposeAsync()
    {
        await states.EnableAsync(WmsModule.Id);
        await using (var context = await contexts.CreateDbContextAsync())
        {
            await context.Uoms.Where(row => row.Code == uomCode || row.Code == maximumLabelUomCode).ExecuteDeleteAsync();
            await context.StorageAttributes.Where(row => row.Code == storageAttributeCode).ExecuteDeleteAsync();
            await context.ItemFamilies.Where(row => row.Code == itemFamilyCode).ExecuteDeleteAsync();
        }
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SecurityDbContext>().Users.Where(user => users.Contains(user.Id)).ExecuteDeleteAsync();
        client.Dispose();
    }

    [Fact]
    public async Task AllReadCapabilitiesReturnTheExactDirectCollectionAndUniqueInvocationIds()
    {
        var invocationIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (capability, path, code) in Cases())
        {
            var response = await Send(HttpMethod.Post, InvocationPath(capability), "{\"input\":{\"limit\":100}}", admin);
            Headers(response, HttpStatusCode.OK);
            var body = await Body(response);
            Assert.Equal(InvocationFields, body.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
            Assert.Equal(capability, body.GetProperty("capabilityId").GetString());
            string invocationId = body.GetProperty("invocationId").GetString()!;
            Assert.False(string.IsNullOrWhiteSpace(invocationId));
            Assert.True(invocationIds.Add(invocationId));

            var output = body.GetProperty("output");
            Assert.Equal(CollectionFields, output.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
            Assert.Equal(0, output.GetProperty("offset").GetInt32());
            Assert.Equal(100, output.GetProperty("limit").GetInt32());
            Assert.Equal("[{\"field\":\"code\",\"direction\":\"asc\"}]", output.GetProperty("order").GetRawText());
            var item = output.GetProperty("items").EnumerateArray().Single(candidate => candidate.GetProperty("code").GetString() == code);
            Assert.NotEqual(Guid.Empty, item.GetProperty("id").GetGuid());
            Assert.Equal(code, item.GetProperty("code").GetString());

            var direct = await Body(await Send(HttpMethod.Get, path + "?limit=100"));
            Assert.Equal(direct.GetRawText(), output.GetRawText());
            Assert.Equal(direct.GetProperty("total").GetRawText(), output.GetProperty("total").GetRawText());
        }
        Assert.Equal(3, invocationIds.Count);
    }

    [Fact]
    public async Task InputDefaultsAndExplicitPaginationMatchTheDirectCollections()
    {
        var defaults = await Body(await Send(HttpMethod.Post, InvocationPath(UomCapability), EmptyInput));
        var defaultOutput = defaults.GetProperty("output");
        Assert.Equal(0, defaultOutput.GetProperty("offset").GetInt32());
        Assert.Equal(CollectionPagination.DefaultLimit, defaultOutput.GetProperty("limit").GetInt32());
        Assert.Equal((await Body(await Send(HttpMethod.Get, UomEndpoints.Path))).GetRawText(), defaultOutput.GetRawText());

        var slice = await Body(await Send(HttpMethod.Post, InvocationPath(UomCapability), "{\"input\":{\"offset\":0,\"limit\":1}}"));
        var sliceOutput = slice.GetProperty("output");
        Assert.Equal(0, sliceOutput.GetProperty("offset").GetInt32());
        Assert.Equal(1, sliceOutput.GetProperty("limit").GetInt32());
        Assert.Single(sliceOutput.GetProperty("items").EnumerateArray());
        Assert.Equal((await Body(await Send(HttpMethod.Get, UomEndpoints.Path + "?offset=0&limit=1"))).GetRawText(), sliceOutput.GetRawText());

        const string hugeOffset = "922337203685477580800000000000000000000";
        var beyond = await Body(await Send(HttpMethod.Post, InvocationPath(UomCapability), "{\"input\":{\"offset\":" + hugeOffset + "}}"));
        var beyondOutput = beyond.GetProperty("output");
        Assert.Empty(beyondOutput.GetProperty("items").EnumerateArray());
        Assert.Equal(hugeOffset, beyondOutput.GetProperty("offset").GetRawText());
        Assert.Equal(CollectionPagination.DefaultLimit, beyondOutput.GetProperty("limit").GetInt32());
    }

    [Fact]
    public async Task MaximumScalarLabelsAreAccepted()
    {
        var all = await Body(await Send(HttpMethod.Get, UomEndpoints.Path + "?offset=0&limit=1"));
        long offset = all.GetProperty("total").GetInt64() - 1;
        var direct = await Body(await Send(HttpMethod.Get, UomEndpoints.Path + "?offset=" + offset + "&limit=1"));
        Assert.Equal(maximumLabelUomCode, direct.GetProperty("items")[0].GetProperty("code").GetString());

        var invocation = await Body(await Send(HttpMethod.Post, InvocationPath(UomCapability),
            "{\"input\":{\"offset\":" + offset + ",\"limit\":1}}"));
        Assert.Equal(direct.GetRawText(), invocation.GetProperty("output").GetRawText());
    }

    [Fact]
    public async Task ClosedInputRejectsInvalidJsonFieldsAndValues()
    {
        foreach (string body in InvalidBodies)
            await Error(await Send(HttpMethod.Post, InvocationPath(UomCapability), body, admin), HttpStatusCode.BadRequest, ModuleEndpoints.InvalidRequest);
    }

    [Fact]
    public async Task AuthenticationEffectiveResolutionAndIdSyntaxPrecedeBodyValidation()
    {
        await Error(await Send(HttpMethod.Post, InvocationPath(UomCapability), "{", ""), HttpStatusCode.Unauthorized, "unauthorized");
        await Error(await Send(HttpMethod.Post, InvocationPath("Wms.uoms.list"), "{", ""), HttpStatusCode.Unauthorized, "unauthorized");

        await Error(await Send(HttpMethod.Post, InvocationPath("Wms.uoms.list"), "{", admin), HttpStatusCode.BadRequest, ModuleEndpoints.InvalidRequest);
        await Error(await Send(HttpMethod.Post, InvocationPath("wms.unknown.list"), "{", admin), HttpStatusCode.NotFound, CapabilityEndpoints.NotFound);
        await Error(await Send(HttpMethod.Post, InvocationPath(UomCapability), "{", ordinary), HttpStatusCode.NotFound, CapabilityEndpoints.NotFound);
        try
        {
            await states.DisableAsync(WmsModule.Id);
            await Error(await Send(HttpMethod.Post, InvocationPath(UomCapability), "{", admin), HttpStatusCode.NotFound, CapabilityEndpoints.NotFound);
            await Error(await Send(HttpMethod.Post, InvocationPath(UomCapability), EmptyInput, admin), HttpStatusCode.NotFound, CapabilityEndpoints.NotFound);
        }
        finally { await states.EnableAsync(WmsModule.Id); }
    }

    [Fact]
    public async Task ProviderExceptionAndKnownInvalidOutputShareTheSafeExecutionFailure()
    {
        await AssertExecutionFailure(() => throw new InvalidOperationException("provider-secret"));
        await AssertExecutionFailure(() => new
        {
            items = Array.Empty<object>(), offset = 0, limit = 0, total = 0,
            order = new[] { new { field = "code", direction = "asc" } }
        });
    }

    [Fact]
    public async Task AuditRecordsAttemptsAndResultsWithoutPayloadsOrInvocationIdOnErrors()
    {
        var capture = new CaptureLoggerProvider();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Information);
            logging.AddProvider(capture);
        }));
        using var auditClient = host.CreateClient();
        string secret = "audit-secret-" + Guid.NewGuid().ToString("N");
        await Error(await Send(auditClient, HttpMethod.Post, InvocationPath(UomCapability),
            "{\"input\":{\"limit\":1},\"secret\":\"" + secret + "\"}", ""), HttpStatusCode.Unauthorized, "unauthorized");
        Headers(await Send(auditClient, HttpMethod.Post, InvocationPath(UomCapability), EmptyInput, admin), HttpStatusCode.OK);
        await Error(await Send(auditClient, HttpMethod.Post, InvocationPath(UomCapability), "{", ordinary),
            HttpStatusCode.NotFound, CapabilityEndpoints.NotFound);
        await Error(await Send(auditClient, HttpMethod.Post, InvocationPath(UomCapability), "{}", admin),
            HttpStatusCode.BadRequest, ModuleEndpoints.InvalidRequest);

        string[] messages = capture.Messages.ToArray();
        Assert.Contains(messages, message => message.Contains("outcome=completed")
            && message.Contains("capability=" + UomCapability)
            && message.Contains("module=" + WmsModule.Id, StringComparison.Ordinal)
            && message.Contains("subject=" + adminId, StringComparison.Ordinal)
            && message.Contains("timestamp=")
            && message.Contains("durationMs=")
            && message.Contains("correlationId="));
        Assert.Contains(messages, message => message.Contains("outcome=denied")
            && message.Contains("error=unauthorized")
            && message.Contains("capability= ")
            && message.Contains("subject= "));
        Assert.Contains(messages, message => message.Contains("outcome=denied")
            && message.Contains("error=capability_not_found")
            && message.Contains("capability= "));
        Assert.Contains(messages, message => message.Contains("outcome=denied")
            && message.Contains("error=invalid_request")
            && message.Contains("capability=" + UomCapability));
        Assert.DoesNotContain(secret, string.Join("\n", messages));
    }

    [Fact]
    public async Task AuditRecordsExecutionFailuresWithInternalInvocationIdButNoPublicIdentifier()
    {
        var capture = new CaptureLoggerProvider();
        using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICapabilityExecutor>();
                services.AddSingleton<ICapabilityExecutor>(new StubExecutor(UomCapability,
                    () => throw new InvalidOperationException("audit-provider-secret")));
            });
            builder.ConfigureLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Information);
                logging.AddProvider(capture);
            });
        });
        using var auditClient = host.CreateClient();
        var response = await Send(auditClient, HttpMethod.Post, InvocationPath(UomCapability), EmptyInput, admin);
        await Error(response, HttpStatusCode.InternalServerError, CapabilityEndpoints.ExecutionFailed);
        var message = Assert.Single(capture.Messages, candidate => candidate.Contains("outcome=failed"));
        Assert.Contains("error=capability_execution_failed", message);
        Assert.Contains("capability=" + UomCapability, message);
        Assert.DoesNotContain("audit-provider-secret", message);
        Assert.DoesNotContain("\"output\"", message);
    }

    private IEnumerable<(string Capability, string Path, string Code)> Cases()
    {
        yield return (UomCapability, UomEndpoints.Path, uomCode);
        yield return (StorageAttributeCapability, StorageAttributeEndpoints.Path, storageAttributeCode);
        yield return (ItemFamilyCapability, ItemFamilyEndpoints.Path, itemFamilyCode);
    }

    private async Task AssertExecutionFailure(Func<object?> execution)
    {
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ICapabilityExecutor>();
            services.AddSingleton<ICapabilityExecutor>(new StubExecutor(UomCapability, execution));
        }));
        using var faultyClient = host.CreateClient();
        var response = await Send(faultyClient, HttpMethod.Post, InvocationPath(UomCapability), EmptyInput, admin);
        await Error(response, HttpStatusCode.InternalServerError, CapabilityEndpoints.ExecutionFailed);
        string text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("invocationId", text);
        Assert.DoesNotContain("provider-secret", text);
    }

    private static string InvocationPath(string capabilityId) => CapabilityEndpoints.Path + "/" + capabilityId + "/invocations";

    private Task<HttpResponseMessage> Send(HttpMethod method, string path, string? body = null, string? token = null) =>
        Send(client, method, path, body, token ?? admin);

    private static async Task<HttpResponseMessage> Send(HttpClient http, HttpMethod method, string path, string? body = null, string? token = null)
    {
        using var request = new HttpRequestMessage(method, path);
        string bearer = token ?? "";
        if (bearer.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, bearer);
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, MediaTypeNames.Application.Json);
        return await http.SendAsync(request);
    }

    private static async Task<JsonElement> Body(HttpResponseMessage response)
    {
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task Error(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Headers(response, status);
        ContractErrors.Single(await Body(response), code);
    }

    private static void Headers(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        if (status == HttpStatusCode.Unauthorized) Assert.Equal(BearerScheme, Assert.Single(response.Headers.WwwAuthenticate).Scheme);
        else Assert.Empty(response.Headers.WwwAuthenticate);
        Assert.Null(response.Headers.RetryAfter);
    }

    private sealed class StubExecutor(string capabilityId, Func<object?> execute) : ICapabilityExecutor
    {
        public bool CanExecute(string requested) => string.Equals(requested, capabilityId, StringComparison.Ordinal);

        public Task<object?> ExecuteAsync(CapabilityDescriptor capability, CapabilityListInput input,
            CapabilityExecutionContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(execute());
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

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (category.EndsWith(nameof(CapabilityInvocationAuditMiddleware), StringComparison.Ordinal))
                    messages.Enqueue(formatter(state, exception));
            }
        }
    }
}
