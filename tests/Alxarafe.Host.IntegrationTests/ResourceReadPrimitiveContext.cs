using System.Collections.Concurrent;
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
using Alxarafe.Modules.Ai.Http;
using Alxarafe.Modules.Assets;
using Alxarafe.Modules.Wms;
using Alxarafe.Security;
using Alxarafe.Security.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class ResourceReadPrimitiveContext : IAsyncLifetime
{
    private const string Password = "Resource-read-fixture-123!";
    public const string SecondaryTenant = "resource-read-secondary";
    private readonly string prefix = "ai_resource_test_" + Guid.NewGuid().ToString("N");
    private readonly List<string> databases = [];
    private string adminConnection = "";
    private WebApplicationFactory<Program> application = null!;
    public HttpClient Client { get; private set; } = null!;
    public string AdminToken { get; private set; } = "";
    public string ReaderToken { get; private set; } = "";
    public string InvokeOnlyToken { get; private set; } = "";
    public Guid ReaderId { get; private set; }
    public IServiceProvider Services => application.Services;
    public Func<AiModelRequest, Task<AiModelResponse>> Complete { get; set; } = _ => Task.FromResult(new AiModelResponse("Ready.", []));
    public Audit Capture { get; } = new();
    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("ConnectionStrings__Tenant_development")
            ?? throw new InvalidOperationException("Explicit PostgreSQL test connection required.");
        var connection = new NpgsqlConnectionStringBuilder(configured) { Database = "postgres", Pooling = false };
        adminConnection = connection.ConnectionString;
        var settings = new Dictionary<string, string?>
        {
            [AssetsDemoSeed.EnabledKey] = "true",
            ["Tenancy:DevelopmentBootstrap"] = "true",
            ["SecuritySeed:AdminEmail"] = "resource-admin@example.test",
            ["SecuritySeed:AdminPassword"] = Password
        };
        foreach (var (key, suffix) in new[] { ("Platform", "platform"), ("Security", "security"), ("Tenant_development", "business"), ("Tenant_test_b", "second") })
        {
            string name = prefix + "_" + suffix;
            await using var admin = new NpgsqlConnection(adminConnection); await admin.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin); await command.ExecuteNonQueryAsync();
            databases.Add(name); connection.Database = name; settings["ConnectionStrings:" + key] = connection.ConnectionString;
        }
        application = new TenantApplicationFactory().WithWebHostBuilder(builder => builder.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings))
            .ConfigureServices(services =>
            {
                services.RemoveAll<IAiModelClient>(); services.AddSingleton<IAiModelClient>(new Script(this));
                services.AddLogging(logging => logging.AddProvider(Capture));
            }));
        Client = application.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await Services.GetRequiredService<ModuleStateService>().EnableAsync(AssetsModule.Id);
        await Services.GetRequiredService<ModuleStateService>().EnableAsync(WmsModule.Id);
        AdminToken = await Login("resource-admin@example.test");
        ReaderId = await User("resource-reader@example.test", [AiContract.AssistantPermission, AssetsPermissions.Read]);
        await User("resource-invoke-only@example.test", [AiContract.AssistantPermission]);
        ReaderToken = await Login("resource-reader@example.test"); InvokeOnlyToken = await Login("resource-invoke-only@example.test");
        await using var platform = await Services.GetRequiredService<IDbContextFactory<PlatformDbContext>>().CreateDbContextAsync();
        platform.Tenants.Add(new TenantRow { Id = SecondaryTenant, Key = SecondaryTenant, DisplayName = "READ test tenant", Active = true, DatabaseReference = "test_b" });
        await platform.SaveChangesAsync();
        await Services.GetRequiredService<AssetsModule>().InitializeAsync(Services);
    }
    public async Task DisposeAsync()
    {
        Client?.Dispose(); if (application is not null) await application.DisposeAsync(); Capture.Dispose();
        foreach (string database in databases)
        {
            await using var admin = new NpgsqlConnection(adminConnection); await admin.OpenAsync();
            await using var command = new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)", admin); await command.ExecuteNonQueryAsync();
        }
    }
    private sealed class Script(ResourceReadPrimitiveContext owner) : IAiModelClient
    {
        public Task<AiModelResponse> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken = default) => owner.Complete(request);
    }
    public async Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body = null, string? token = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token ?? ReaderToken);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await Client.SendAsync(request);
    }
    public Task<HttpResponseMessage> Ask(string message, string? token = null) => Send(HttpMethod.Post, AiAssistantEndpoints.Path, new { message }, token);
    private async Task<string> Login(string email)
    {
        using var response = await Client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
    }
    private async Task<Guid> User(string email, string[] permissions)
    {
        using var response = await Send(HttpMethod.Post, "/api/users", new { email, password = Password, admin = false }, AdminToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await using var scope = Services.CreateAsyncScope(); var manager = scope.ServiceProvider.GetRequiredService<UserManager<AlxarafeUser>>();
        var user = (await manager.FindByIdAsync(id.ToString()))!;
        foreach (string permission in permissions) Assert.True((await manager.AddClaimAsync(user, new Claim(PermissionClaim.Type, permission))).Succeeded);
        return id;
    }
    public async Task Permission(bool grant)
    {
        await using var scope = Services.CreateAsyncScope(); var manager = scope.ServiceProvider.GetRequiredService<UserManager<AlxarafeUser>>();
        var user = (await manager.FindByIdAsync(ReaderId.ToString()))!;
        var claim = new Claim(PermissionClaim.Type, AssetsPermissions.Read);
        Assert.True((grant ? await manager.AddClaimAsync(user, claim) : await manager.RemoveClaimAsync(user, claim)).Succeeded);
    }
    public async Task Membership(string tenant)
    {
        await using var db = await Services.GetRequiredService<IDbContextFactory<PlatformDbContext>>().CreateDbContextAsync();
        await db.TenantMemberships.Where(row => row.UserId == ReaderId).ExecuteDeleteAsync();
        db.TenantMemberships.Add(new TenantMembershipRow { UserId = ReaderId, TenantId = tenant, Active = true }); await db.SaveChangesAsync();
    }
    public async Task<ReadPrimitiveResult> Probe(string id, JsonElement input, string? token = null, Action<AiModelRequest>? inspect = null)
    {
        ReadPrimitiveResult? result = null;
        Complete = async request =>
        {
            inspect?.Invoke(request);
            var http = Services.GetRequiredService<IHttpContextAccessor>().HttpContext!;
            result = await http.RequestServices.GetRequiredService<IReadPrimitiveService>().InvokeAsync(new(id, input, http.User, request.InvocationId!, "resource-read-probe", 1));
            return new("Checked.", []);
        };
        using var response = await Ask("Check authorized READ", token); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return result!;
    }
    public sealed class Audit : ILoggerProvider
    {
        public ConcurrentQueue<string> Lines { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Sink(Lines, categoryName.EndsWith("ReadPrimitiveService", StringComparison.Ordinal));
        public void Dispose() { }
        private sealed class Sink(ConcurrentQueue<string> lines, bool enabled) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => enabled && logLevel >= LogLevel.Information;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            { if (IsEnabled(logLevel)) lines.Enqueue(formatter(state, exception)); }
        }
    }
}
