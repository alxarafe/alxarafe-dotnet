using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Alxarafe.Core;
using Alxarafe.EntityFrameworkCore;
using Alxarafe.Modularity;
using Alxarafe.Modules.Ai;
using Alxarafe.Modules.Wms;
using Alxarafe.Resources;
using Alxarafe.Resources.AspNetCore;
using Alxarafe.Resources.Runtime;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class ResourceActionApiTests(ResourceApiTestContext context) : IClassFixture<ResourceApiTestContext>
{
    private const string Resource = "fixture.actions";
    private const string Platform = "fixture.platform-actions";
    private const string Post = "invoice.post";
    private const string Import = "item.import";
    private const string Close = "order.close";
    private const string Reason = "invoice_already_posted";
    private const string Entity = """{"target":{"id":"035a2161-c936-42dc-8b4b-d3cf5d1953a6"},"parameters":{"reason":"valid"}}""";
    private const string EntityAvailability = """{"scope":"ENTITY","id":"035a2161-c936-42dc-8b4b-d3cf5d1953a6"}""";
    private sealed class State
    {
        internal int Executions;
        internal int Evaluations;
        internal bool Available = true;
        internal bool ProviderThrows;
        internal bool HandlerThrows;
        internal bool Conflict;
        internal ResourceActionExecutionContext? Received;
    }
    private sealed class Definitions : IResourceDefinitionProvider
    {
        public IReadOnlyCollection<ResourceDefinition> GetResourceDefinitions() => [Definition(Resource, WmsModule.Id), Definition(Platform, AiModule.Id)];
        private static ResourceDefinition Definition(string id, string module) => new(id, ResourceApiFixtures.Version, module, new("id"),
            [new("id", ScalarType.Uuid, readOnly: true, immutable: true)], [], new([]), actions:
            [new(Post, ActionScope.Entity, ResourceApiFixtures.Write, parameters:
                [new("reason", ScalarType.String, required: true, constraints: new TextConstraints(MaximumLength: 250), annotations: [new("Common", "Label", new AnnotationText("Reason"))]),
                    new("optional", ScalarType.String, nullable: true, defaultValue: new TextValue("default")),
                    new("mode", ScalarType.Enum, enumValues: ["open", "closed"]),
                    new("rating", ScalarType.UInt8, constraints: new NumericConstraints(Maximum: ResourceNumber.Parse("10")))]),
                new(Import, ActionScope.Collection, ResourceApiFixtures.Write, parameters: [new("active", ScalarType.Boolean, defaultValue: new BooleanValue(true))]),
                new(Close, ActionScope.Selection, ResourceApiFixtures.Write, requiresSelection: true),
                new("availability", ActionScope.Collection, ResourceApiFixtures.Write)]);
    }
    private sealed class Handler(string resourceId, string actionName, State state) : IResourceActionHandler
    {
        public string ResourceId => resourceId;
        public string ActionName => actionName;
        public Task<ResourceActionOutcome> ExecuteAsync(ResourceActionExecutionContext input, CancellationToken cancellationToken = default)
        {
            state.Executions++; state.Received = input;
            if (state.HandlerThrows) throw new InvalidOperationException("secret SQL credential");
            if (input.Context.Target is ResourceEntityTarget { Identifier.Value: UuidValue id } && id.Value == Guid.Empty) return Task.FromResult(new ResourceActionOutcome(ResourceActionOutcomeKind.NotFound));
            return Task.FromResult(state.Conflict ? new ResourceActionOutcome(ResourceActionOutcomeKind.Conflict, Reason) : new(ResourceActionOutcomeKind.Success));
        }
    }
    private sealed class Provider(string resourceId, State state) : IResourceActionAvailabilityProvider
    {
        public string ResourceId => resourceId;
        public string ActionName => Post;
        public Task<ResourceActionAvailability> EvaluateAsync(ResourceActionContext target, CancellationToken cancellationToken = default)
        { state.Evaluations++; if (state.ProviderThrows) throw new InvalidOperationException("secret SQL credential"); return Task.FromResult(new ResourceActionAvailability(state.Available, state.Available ? null : Reason)); }
    }
    private sealed class App : IDisposable
    {
        internal readonly State State = new();
        private readonly Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> _host;
        internal HttpClient Client { get; }
        internal App(ResourceApiTestContext context)
        {
            _host = context.Host.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.AddSingleton<IResourceDefinitionProvider, Definitions>();
                foreach (string resource in new[] { Resource, Platform })
                {
                    foreach (string action in new[] { Post, Import, Close, "availability" }) services.AddScoped<IResourceActionHandler>(_ => new Handler(resource, action, State));
                    services.AddScoped<IResourceActionAvailabilityProvider>(_ => new Provider(resource, State));
                }
            }));
            Client = _host.CreateClient(new() { AllowAutoRedirect = false });
        }
        public void Dispose() { Client.Dispose(); _host.Dispose(); }
        internal async Task<HttpResponseMessage> Send(string path, string json, string token, bool forged = false)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (forged) request.Headers.Add("X-Tenant-Id", DevelopmentTenantBootstrap.TenantId);
            return await Client.SendAsync(request);
        }
    }
    private static string Execute(string action = Post, string resource = Resource) => ResourceActionEndpoints.Path + "/" + resource + "/" + action + ResourceActionEndpoints.ExecutionSegment;
    private static string Availability(string resource = Resource) => ResourceActionEndpoints.Path + "/" + resource + ResourceActionEndpoints.AvailabilitySegment;
    private static async Task<JsonElement> Body(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();
    private static async Task Error(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        using (response) { Assert.Equal(status, response.StatusCode); string text = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain("secret", text, StringComparison.Ordinal); Assert.Equal(code, (await Body(response)).GetProperty("code").GetString()); }
    }
    [Fact]
    public async Task AllScopesExecuteAndDefaultsPreserveExplicitNull()
    {
        using var app = new App(context);
        using var post = await app.Send(Execute(), Entity, context.Writer); Assert.Equal(HttpStatusCode.NoContent, post.StatusCode); Assert.Empty(await post.Content.ReadAsByteArrayAsync());
        Assert.Equal(new TextValue("default"), app.State.Received!.Input.Values["optional"]);
        using var explicitNull = await app.Send(Execute(), Entity.Replace("\"reason\":\"valid\"", "\"reason\":\"valid\",\"optional\":null", StringComparison.Ordinal), context.Writer);
        Assert.Equal(HttpStatusCode.NoContent, explicitNull.StatusCode); Assert.IsType<NullValue>(app.State.Received!.Input.Values["optional"]);
        using var import = await app.Send(Execute(Import), "{}", context.Writer); Assert.Equal(HttpStatusCode.NoContent, import.StatusCode); Assert.Equal(new BooleanValue(true), app.State.Received!.Input.Values["active"]);
        using var selection = await app.Send(Execute(Close), """{"target":{"ids":["035a2161-c936-42dc-8b4b-d3cf5d1953a6","035a2161-c936-42dc-8b4b-d3cf5d1953a7"]}}""", context.Writer);
        Assert.Equal(HttpStatusCode.NoContent, selection.StatusCode); Assert.Equal(2, ((ResourceSelectionTarget)app.State.Received!.Context.Target).Identifiers.Count);
        using var alias = await app.Send(Execute("availability"), "{}", context.Writer); Assert.Equal(HttpStatusCode.NoContent, alias.StatusCode);
    }
    [Theory]
    [InlineData(Post, "{")]
    [InlineData(Post, "{}")]
    [InlineData(Post, "{\"target\":{\"id\":\"bad\"}}")]
    [InlineData(Post, "{\"target\":{\"ids\":[]}}")]
    [InlineData(Post, "{\"target\":{\"id\":\"035a2161-c936-42dc-8b4b-d3cf5d1953a6\"},\"parameters\":{\"reason\":null}}")]
    [InlineData(Post, "{\"target\":{\"id\":\"035a2161-c936-42dc-8b4b-d3cf5d1953a6\"},\"parameters\":{\"unknown\":true}}")]
    [InlineData(Import, "{\"target\":{\"id\":\"035a2161-c936-42dc-8b4b-d3cf5d1953a6\"}}")]
    [InlineData(Close, "{\"target\":{\"ids\":[]}}")]
    [InlineData(Close, "{\"target\":{\"ids\":[\"035a2161-c936-42dc-8b4b-d3cf5d1953a6\",\"035a2161-c936-42dc-8b4b-d3cf5d1953a6\"]}}")]
    [InlineData(Import, "{\"parameters\":{\"active\":\"\\uD800\"}}")]
    [InlineData(Import, "{\"tenantId\":\"forged\"}")]
    [InlineData(Import, "{\"userId\":\"forged\"}")]
    [InlineData(Import, "{\"permissions\":[\"wms.items.write\"]}")]
    [InlineData(Import, "{\"databaseReference\":\"forged\"}")]
    public async Task MalformedAndForgedInputsNeverReachDomain(string action, string json)
    {
        using var app = new App(context); await Error(await app.Send(Execute(action), json, context.Writer), HttpStatusCode.BadRequest, ResourceEndpoints.InvalidRequest); Assert.Equal(0, app.State.Executions); Assert.Equal(0, app.State.Evaluations);
    }
    [Fact]
    public async Task AuthorizationAvailabilityRechecksAndFailureMappingsAreSafe()
    {
        using var app = new App(context);
        using var unauthorized = await app.Send(Availability(), EntityAvailability, context.Reader);
        var denied = Assert.Single((await Body(unauthorized)).EnumerateArray()); Assert.False(denied.GetProperty("authorized").GetBoolean()); Assert.False(denied.GetProperty("available").GetBoolean()); Assert.False(denied.TryGetProperty("unavailableReason", out _)); Assert.Equal(0, app.State.Evaluations);
        await Error(await app.Send(Execute(), Entity, context.Reader), HttpStatusCode.Forbidden, "forbidden");
        using var first = await app.Send(Availability(), EntityAvailability, context.Writer); Assert.True(Assert.Single((await Body(first)).EnumerateArray()).GetProperty("available").GetBoolean());
        app.State.Available = false;
        using var blocked = await app.Send(Execute(), Entity, context.Writer); Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode); var failure = await Body(blocked); Assert.Equal("resource_action_unavailable", failure.GetProperty("code").GetString()); Assert.Equal(Reason, failure.GetProperty("errors")[0].GetProperty("code").GetString()); Assert.Equal(0, app.State.Executions);
        app.State.Available = true;
        try
        {
            await context.SetWriterPermission(false);
            await Error(await app.Send(Execute(), Entity, context.Writer), HttpStatusCode.Forbidden, "forbidden"); Assert.Equal(0, app.State.Executions);
        }
        finally { await context.SetWriterPermission(true); }
        app.State.ProviderThrows = true; await Error(await app.Send(Execute(), Entity, context.Writer), HttpStatusCode.InternalServerError, ResourceEndpoints.ExecutionFailed); Assert.Equal(0, app.State.Executions);
        app.State.ProviderThrows = false; app.State.HandlerThrows = true; await Error(await app.Send(Execute(), Entity, context.Writer), HttpStatusCode.InternalServerError, ResourceEndpoints.ExecutionFailed);
        app.State.HandlerThrows = false; app.State.Conflict = true; await Error(await app.Send(Execute(), Entity, context.Writer), HttpStatusCode.Conflict, "resource_action_conflict");
        app.State.Conflict = false; await Error(await app.Send(Execute(), Entity.Replace(ResourceApiFixtures.FirstId.ToString("D"), Guid.Empty.ToString("D"), StringComparison.Ordinal), context.Writer), HttpStatusCode.NotFound, ResourceEndpoints.NotFound);
        await Error(await app.Send(Execute("unknown"), Entity, context.Writer), HttpStatusCode.NotFound, "resource_action_not_found");
        await Error(await app.Send(Execute(resource: "unknown"), Entity, context.Writer), HttpStatusCode.NotFound, ResourceEndpoints.NotFound);
    }
    [Fact]
    public async Task TenantModuleAndMembershipAuthorityIsPreservedWhilePlatformNeedsNoTenant()
    {
        using var app = new App(context);
        await Error(await app.Send(Execute(), Entity, context.NoTenant, forged: true), HttpStatusCode.Forbidden, TenantAccessException.Required);
        await Error(await app.Send(Execute(), Entity, context.MultipleTenants, forged: true), HttpStatusCode.Forbidden, TenantAccessException.SelectionRequired);
        await using var scope = context.Host.Services.CreateAsyncScope();
        await using var platform = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<PlatformDbContext>>().CreateDbContextAsync();
        try
        {
            await platform.TenantMemberships.Where(row => row.UserId == context.WriterId).ExecuteUpdateAsync(update => update.SetProperty(row => row.Active, false));
            await Error(await app.Send(Execute(), Entity, context.Writer, forged: true), HttpStatusCode.Forbidden, TenantAccessException.Required);
        }
        finally { await platform.TenantMemberships.Where(row => row.UserId == context.WriterId).ExecuteUpdateAsync(update => update.SetProperty(row => row.Active, true)); }
        var modules = context.Host.Services.GetRequiredService<ModuleStateService>();
        try
        {
            await modules.DisableAsync(WmsModule.Id);
            await Error(await app.Send(Availability(), EntityAvailability, context.Writer), HttpStatusCode.ServiceUnavailable, "module_disabled");
            await Error(await app.Send(Execute(), Entity, context.Writer), HttpStatusCode.ServiceUnavailable, "module_disabled");
        }
        finally { await modules.EnableAsync(WmsModule.Id); }
        Assert.Equal(0, app.State.Executions); Assert.Equal(0, app.State.Evaluations);
        using var valid = await app.Send(Execute(Import, Platform), "{}", context.NoTenant); Assert.Equal(HttpStatusCode.NoContent, valid.StatusCode);
    }
    [Fact]
    public async Task MetadataAndOpenApiDeriveActionParametersAndTargetsFromSameDefinition()
    {
        using var app = new App(context);
        using var metadataRequest = new HttpRequestMessage(HttpMethod.Get, ResourceEndpoints.MetadataPath + "/" + Resource); metadataRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.Writer);
        using var response = await app.Client.SendAsync(metadataRequest); var metadata = await Body(response);
        var action = metadata.GetProperty("actions").EnumerateArray().Single(action => action.GetProperty("name").GetString() == Post);
        Assert.Equal(4, action.GetProperty("parameters").GetArrayLength()); Assert.False(action.TryGetProperty("available", out _));
        var reason = action.GetProperty("parameters").EnumerateArray().Single(parameter => parameter.GetProperty("name").GetString() == "reason"); Assert.True(reason.GetProperty("required").GetBoolean()); Assert.Equal(250, reason.GetProperty("constraints").GetProperty("maximumLength").GetInt32()); Assert.NotEmpty(reason.GetProperty("annotations").EnumerateArray());
        using var openapiResponse = await app.Client.GetAsync("/openapi/v1.json"); var paths = (await Body(openapiResponse)).GetProperty("paths");
        var operation = paths.GetProperty(Execute()).GetProperty("post"); Assert.True(operation.GetProperty("responses").TryGetProperty("204", out _)); Assert.True(operation.GetProperty("responses").TryGetProperty("409", out _)); Assert.True(operation.GetProperty("responses").TryGetProperty("503", out _));
        var body = operation.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema"); var parameters = body.GetProperty("properties").GetProperty("parameters");
        Assert.Equal(10, parameters.GetProperty("properties").GetProperty("rating").GetProperty("maximum").GetInt32()); Assert.Equal(2, parameters.GetProperty("properties").GetProperty("mode").GetProperty("enum").GetArrayLength());
        Assert.Contains(parameters.GetProperty("required").EnumerateArray(), value => value.GetString() == "reason"); Assert.Equal("uuid", body.GetProperty("properties").GetProperty("target").GetProperty("properties").GetProperty("id").GetProperty("format").GetString());
        Assert.True(paths.TryGetProperty(Availability(), out _));
        var selection = paths.GetProperty(Execute(Close)).GetProperty("post").GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("properties").GetProperty("target").GetProperty("properties").GetProperty("ids"); Assert.Equal(ResourceActionRuntime.MaximumSelection, selection.GetProperty("maxItems").GetInt32());
        Assert.Equal(0, app.State.Evaluations); Assert.Equal(0, app.State.Executions);
    }
}
