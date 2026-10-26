using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Alxarafe.Core;
using Alxarafe.EntityFrameworkCore;
using Alxarafe.Modularity;
using Alxarafe.Modules.Assets;
using Alxarafe.Navigation;
using Alxarafe.Resources;
using Alxarafe.Resources.Runtime;
using Alxarafe.Resources.AspNetCore;
using Alxarafe.Security;
using Alxarafe.Security.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class NavigationTests(AssetsTestContext context) : IClassFixture<AssetsTestContext>
{
    private static readonly string[] InternalResources = [AssetResources.FieldValue, AssetResources.FieldDefinition, AssetResources.Assignment];
    private sealed class Provider(params NavigationDefinition[] definitions) : INavigationDefinitionProvider
    { public IReadOnlyCollection<NavigationDefinition> GetNavigationDefinitions() => definitions; }
    private static async Task<JsonElement> Body(HttpResponseMessage response) => JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsByteArrayAsync());
    private async Task<HttpResponseMessage> Get(string? token, string? etag = null, string? language = null, HttpClient? client = null, string? query = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, NavigationEndpoints.Path + query);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (etag is not null) request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        if (language is not null) request.Headers.TryAddWithoutValidation("Accept-Language", language);
        return await (client ?? context.Client).SendAsync(request);
    }
    private static JsonElement[] Roots(JsonElement navigation) => navigation.GetProperty("items").EnumerateArray().ToArray();
    private static JsonElement[] Children(JsonElement item) => item.GetProperty("children").EnumerateArray().ToArray();
    private static IEnumerable<JsonElement> Flatten(JsonElement navigation)
    {
        IEnumerable<JsonElement> Walk(JsonElement item) => new[] { item }.Concat(Children(item).SelectMany(Walk));
        return Roots(navigation).SelectMany(Walk);
    }

    [Fact]
    public async Task EffectiveAssetsHierarchyIsCuratedAndResourceTargetsRemainMetadataReferences()
    {
        using var response = await Get(context.Full); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Body(response); var root = Assert.Single(Roots(body));
        Assert.Equal(AssetsNavigation.Root, root.GetProperty("id").GetString()); Assert.Equal("Assets", root.GetProperty("label").GetString());
        Assert.False(root.TryGetProperty("target", out _));
        var children = Children(root); Assert.Equal([AssetsNavigation.Register, AssetsNavigation.Types, AssetsNavigation.Locations], children.Select(child => child.GetProperty("id").GetString()));
        Assert.Equal("Asset register", children[0].GetProperty("label").GetString());
        foreach (var child in children)
        {
            var target = child.GetProperty("target"); Assert.Equal("RESOURCE", target.GetProperty("type").GetString());
            string resource = target.GetProperty("resource").GetString()!;
            Assert.True(context.Host.Services.GetRequiredService<ResourceRegistry>().TryGet(resource, out _));
            Assert.DoesNotContain(resource, InternalResources);
            using var metadata = await context.Raw(HttpMethod.Get, "/api/meta/resources/" + resource, null, context.Full); Assert.Equal(HttpStatusCode.OK, metadata.StatusCode);
            Assert.Empty(Children(child)); Assert.False(child.TryGetProperty("fields", out _)); Assert.False(child.TryGetProperty("operations", out _));
        }
        Assert.DoesNotContain("permissions", body.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PermissionsHideActionableEntriesAndPruneEmptyGroups()
    {
        using var reader = await Get(context.Reader); var body = await Body(reader);
        Assert.Equal([AssetsNavigation.Register, AssetsNavigation.Locations], Children(Assert.Single(Roots(body))).Select(child => child.GetProperty("id").GetString()));
        using var denied = await Get(context.NoPermission); Assert.Empty(Roots(await Body(denied)));
        using var noTenant = await Get(context.NoTenant); Assert.Equal(HttpStatusCode.OK, noTenant.StatusCode); Assert.Empty(Roots(await Body(noTenant)));
        using var schema = await Get(context.Schema); Assert.Equal(3, Children(Assert.Single(Roots(await Body(schema)))).Length);
    }

    [Fact]
    public async Task CacheValidationIsUserLanguageAndEffectivePermissionAware()
    {
        using var initial = await Get(context.Full); var body = await Body(initial); string tag = initial.Headers.ETag!.ToString();
        Assert.Equal('"' + body.GetProperty("version").GetString() + '"', tag);
        Assert.True(initial.Headers.CacheControl!.Private); Assert.True(initial.Headers.CacheControl.NoCache); Assert.Contains("Authorization", initial.Headers.Vary); Assert.Contains("Accept-Language", initial.Headers.Vary);
        using var repeat = await Get(context.Full); Assert.Equal(tag, repeat.Headers.ETag!.ToString());
        using var same = await Get(context.Full, tag); Assert.Equal(HttpStatusCode.NotModified, same.StatusCode); Assert.Empty(await same.Content.ReadAsByteArrayAsync());
        using var weak = await Get(context.Full, "W/" + tag); Assert.Equal(HttpStatusCode.NotModified, weak.StatusCode);
        using var reader = await Get(context.Reader, tag); Assert.Equal(HttpStatusCode.OK, reader.StatusCode); Assert.NotEqual(tag, reader.Headers.ETag!.ToString());
        using var sameMenuOtherIdentity = await Get(context.Schema, tag); Assert.Equal(HttpStatusCode.OK, sameMenuOtherIdentity.StatusCode); Assert.NotEqual(tag, sameMenuOtherIdentity.Headers.ETag!.ToString());
        using var logout = await Get(null, tag); Assert.Equal(HttpStatusCode.Unauthorized, logout.StatusCode);
        await using var scope = context.Host.Services.CreateAsyncScope(); var manager = scope.ServiceProvider.GetRequiredService<UserManager<AlxarafeUser>>(); var account = (await manager.FindByIdAsync(context.FullUser.ToString()))!;
        Assert.True((await manager.RemoveClaimAsync(account, new Claim(PermissionClaim.Type, AssetsPermissions.Read))).Succeeded);
        try
        {
            using var revoked = await Get(context.Full, tag); Assert.Equal(HttpStatusCode.OK, revoked.StatusCode); Assert.Empty(Roots(await Body(revoked))); Assert.NotEqual(tag, revoked.Headers.ETag!.ToString());
            using var staleTarget = await context.Raw(HttpMethod.Get, ResourceEndpoints.ResourcePath + "/" + AssetResources.Asset, null, context.Full); Assert.Equal(HttpStatusCode.Forbidden, staleTarget.StatusCode);
        }
        finally { Assert.True((await manager.AddClaimAsync(account, new Claim(PermissionClaim.Type, AssetsPermissions.Read))).Succeeded); }
    }

    [Fact]
    public async Task DisabledModuleIsRemovedBeforeConditionalGetAndRestorationRecoversVersion()
    {
        using var initial = await Get(context.Full); string tag = initial.Headers.ETag!.ToString(); var modules = context.Host.Services.GetRequiredService<ModuleStateService>();
        await modules.DisableAsync(AssetsModule.Id);
        try { using var disabled = await Get(context.Full, tag); Assert.Equal(HttpStatusCode.OK, disabled.StatusCode); Assert.Empty(Roots(await Body(disabled))); }
        finally { await modules.EnableAsync(AssetsModule.Id); }
        using var restored = await Get(context.Full, tag); Assert.Equal(HttpStatusCode.NotModified, restored.StatusCode);
    }

    [Fact]
    public async Task TenantSwitchForTheSameIdentityCannotReusePreviousNavigationVersion()
    {
        using var initial = await Get(context.Full); string tag = initial.Headers.ETag!.ToString();
        using var me = await context.Raw(HttpMethod.Get, "/api/auth/me", null, context.OtherTenant); var otherUser = (await Body(me)).GetProperty("id").GetGuid();
        await using var platform = await context.Host.Services.GetRequiredService<IDbContextFactory<PlatformDbContext>>().CreateDbContextAsync();
        string secondary = await platform.TenantMemberships.Where(row => row.UserId == otherUser).Select(row => row.TenantId).SingleAsync();
        var original = await platform.TenantMemberships.Where(row => row.UserId == context.FullUser).ToArrayAsync();
        platform.TenantMemberships.RemoveRange(original); await platform.SaveChangesAsync();
        platform.TenantMemberships.Add(new TenantMembershipRow { UserId = context.FullUser, TenantId = secondary, Active = true }); await platform.SaveChangesAsync();
        try
        {
            using var changed = await Get(context.Full, tag); Assert.Equal(HttpStatusCode.OK, changed.StatusCode); Assert.NotEqual(tag, changed.Headers.ETag!.ToString());
            Assert.Equal((await Body(initial)).GetProperty("items").GetRawText(), (await Body(changed)).GetProperty("items").GetRawText());
        }
        finally { await platform.TenantMemberships.Where(row => row.UserId == context.FullUser).ExecuteDeleteAsync(); platform.ChangeTracker.Clear(); platform.TenantMemberships.AddRange(original); await platform.SaveChangesAsync(); }
    }

    [Fact]
    public async Task LabelsUseExistingLocalizationWithParentAndDefaultFallback()
    {
        using var english = await Get(context.Full, language: "en"); using var spanish = await Get(context.Full, english.Headers.ETag!.ToString(), "es-MX");
        Assert.Equal(HttpStatusCode.OK, spanish.StatusCode); Assert.Equal("Activos", Assert.Single(Roots(await Body(spanish))).GetProperty("label").GetString());
        Assert.Equal("Registro de activos", Children(Assert.Single(Roots(await Body(spanish))))[0].GetProperty("label").GetString());
        using var fallback = await Get(context.Full, language: "fr"); Assert.Equal(english.Headers.ETag!.ToString(), fallback.Headers.ETag!.ToString());
    }

    [Fact]
    public async Task RouteTargetsAndTiedOrdersAreIndependentOfResourceSchemasAndRegistrationOrder()
    {
        const string group = AssetsModule.Id + ".test-group";
        var definitions = new[] { new NavigationDefinition(group, AssetsModule.Id, "Group", 1),
            new NavigationDefinition(group + ".z", AssetsModule.Id, "Z", 5, group, new RouteNavigationTarget("/configuration"), permissions: [AssetsPermissions.Read]),
            new NavigationDefinition(group + ".a", AssetsModule.Id, "A", 5, group, new RouteNavigationTarget("/dashboard"), icon: "opaque:icon", permissions: [AssetsPermissions.Read]) };
        using var firstHost = context.Host.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddSingleton<INavigationDefinitionProvider>(new Provider(definitions))));
        using var secondHost = context.Host.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddSingleton<INavigationDefinitionProvider>(new Provider(definitions.Reverse().ToArray()))));
        using var a = firstHost.CreateClient(); using var b = secondHost.CreateClient();
        using var first = await Get(context.Reader, client: a); using var second = await Get(context.Reader, client: b);
        Assert.Equal(first.Headers.ETag!.ToString(), second.Headers.ETag!.ToString());
        var custom = Roots(await Body(first))[0]; Assert.Equal(group, custom.GetProperty("id").GetString());
        Assert.Equal([group + ".a", group + ".z"], Children(custom).Select(item => item.GetProperty("id").GetString()));
        Assert.Equal("ROUTE", Children(custom)[0].GetProperty("target").GetProperty("type").GetString());
        Assert.Equal("/dashboard", Children(custom)[0].GetProperty("target").GetProperty("route").GetString());
        using var platformOnly = await Get(context.NoTenant, client: a); Assert.Single(Roots(await Body(platformOnly)));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("cycle")]
    [InlineData("orphan")]
    [InlineData("owner")]
    [InlineData("resource")]
    [InlineData("route")]
    [InlineData("route-permission")]
    [InlineData("leaf-parent")]
    public void InvalidDeclarationsAreRejectedAtRegistryConstruction(string fault)
    {
        string id = AssetsModule.Id + ".invalid";
        NavigationDefinition row = fault switch
        {
            "cycle" => new(id, AssetsModule.Id, "Bad", parentId: id),
            "orphan" => new(id, AssetsModule.Id, "Bad", parentId: id + ".missing"),
            "owner" => new(id, "missing.owner", "Bad"),
            "resource" => new(id, AssetsModule.Id, "Bad", target: new ResourceNavigationTarget("missing.resource")),
            "route" => new(id, AssetsModule.Id, "Bad", target: new RouteNavigationTarget("//"), permissions: [AssetsPermissions.Read]),
            "route-permission" => new(id, AssetsModule.Id, "Bad", target: new RouteNavigationTarget("/dashboard")),
            _ => new(id, AssetsModule.Id, "Bad", target: new ResourceNavigationTarget(AssetResources.Asset))
        };
        NavigationDefinition[] rows = fault == "duplicate" ? [row, row] : fault == "leaf-parent" ? [row, new(id + ".child", AssetsModule.Id, "Child", parentId: id)] : [row];
        Assert.Throws<InvalidOperationException>(() => new NavigationRegistry(context.Host.Services.GetRequiredService<ModuleRegistry>(), context.Host.Services.GetRequiredService<ResourceRegistry>(),
            context.Host.Services.GetRequiredService<ResourceRepositoryCatalog>(), [new Provider(rows)]));
    }

    [Fact]
    public void InvalidContributionsFailActualHostStartup()
    {
        using var bad = context.Host.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddSingleton<INavigationDefinitionProvider>(new Provider(new NavigationDefinition(AssetsModule.Id + ".invalid", AssetsModule.Id, "Bad", target: new ResourceNavigationTarget("missing.resource"))))));
        Assert.Throws<InvalidOperationException>(() => bad.CreateClient());
    }

    private sealed class UnavailableTenant : IRequestTenantContext
    {
        public string? TenantId => null;
        public Task<Tenant> RequireAsync(CancellationToken cancellationToken = default) => throw new TenantAccessException(TenantAccessException.DatabaseUnavailable, configurationFailure: true);
    }
    [Fact]
    public async Task EndpointIsDocumentedAndErrorsUseTheCommonEnvelope()
    {
        using var openapi = await context.Raw(HttpMethod.Get, "/openapi/v1.json", null, context.Full);
        Assert.True((await Body(openapi)).GetProperty("paths").GetProperty(NavigationEndpoints.Path).GetProperty("get").GetProperty("responses").TryGetProperty("304", out _));
        using var invalid = await Get(context.Full, query: "?unexpected=true"); Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode); Assert.Equal("invalid_request", (await Body(invalid)).GetProperty("code").GetString());
        using var host = context.Host.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddScoped<IRequestTenantContext, UnavailableTenant>())); using var client = host.CreateClient();
        using var unavailable = await Get(context.Full, client: client); Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.Equal(TenantAccessException.DatabaseUnavailable, (await Body(unavailable)).GetProperty("code").GetString());
        Assert.Equal(["code", "errors"], (await Body(unavailable)).EnumerateObject().Select(property => property.Name).Order());
    }
}
