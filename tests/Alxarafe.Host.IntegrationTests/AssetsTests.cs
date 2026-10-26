using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Alxarafe.Core;
using Alxarafe.EntityFrameworkCore;
using Alxarafe.Modularity;
using Alxarafe.Modules.Assets;
using Alxarafe.Resources;
using Alxarafe.Resources.AspNetCore;
using Alxarafe.Resources.Runtime;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class AssetsTests(AssetsTestContext context) : IClassFixture<AssetsTestContext>
{
    private const int MaximumCondition = 100;
    private static Guid Id(JsonElement record) => record.GetProperty(AssetFields.Id).GetGuid();
    private static async Task<JsonElement> Body(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();
    private static async Task Error(HttpResponseMessage response, HttpStatusCode status)
    {
        using (response)
        {
            Assert.Equal(status, response.StatusCode);
            var body = await Body(response);
            Assert.NotEmpty(body.GetProperty("errors").EnumerateArray());
            if (status == HttpStatusCode.Conflict) Assert.Contains(body.GetProperty("code").GetString(), new[] { ResourceEndpoints.Conflict, "resource_action_conflict" });
            if (status == HttpStatusCode.BadRequest) Assert.Equal(ResourceEndpoints.InvalidRequest, body.GetProperty("code").GetString());
        }
    }
    private async Task<JsonElement> Get(Guid id, string? token = null)
    {
        using var response = await context.Send(HttpMethod.Get, AssetResources.Asset, id, token: token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); return await Body(response);
    }

    [Fact]
    public async Task MetadataAndAdaptersComeFromProductionModuleComposition()
    {
        var registry = context.Host.Services.GetRequiredService<ResourceRegistry>();
        Assert.Equal(AssetResources.All.Order(StringComparer.Ordinal), registry.All.Select(resource => resource.Id));
        Assert.All(registry.All, resource => Assert.Equal(AssetsModule.Id, resource.ModuleId));
        Assert.Equal(ModulePersistence.Tenant, context.Host.Services.GetRequiredService<AssetsModule>().Descriptor.Persistence);
        Assert.False(typeof(IHttpModule).IsAssignableFrom(typeof(AssetsModule)));
        await using var scope = context.Host.Services.CreateAsyncScope();
        var adapters = scope.ServiceProvider.GetServices<IResourceRepository>().ToArray();
        Assert.Equal(AssetResources.All.Count, adapters.Length);
        Assert.All(adapters, adapter => Assert.Equal(typeof(AssetsModule).Assembly, adapter.GetType().Assembly));
        using var response = await context.Raw(HttpMethod.Get, ResourceEndpoints.MetadataPath + "/" + AssetResources.Asset, null, context.Reader);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var metadata = await Body(response);
        Assert.Contains(metadata.GetProperty("fields").EnumerateArray(), field => field.GetProperty("name").GetString() == AssetFields.AssetTypeId && field.GetProperty("immutable").GetBoolean());
        Assert.Equal(5, metadata.GetProperty("relations").GetArrayLength());
        await using var db = await context.Database();
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Single(await db.Database.GetAppliedMigrationsAsync());
        Assert.DoesNotContain(db.Model.GetEntityTypes().SelectMany(entity => entity.GetProperties()), property => property.Name == "TenantId");
    }

    [Fact]
    public async Task MinimalCreatePersistsImmediatelyAndSurvivesHostReinitialization()
    {
        var asset = await context.Asset(); var id = Id(asset);
        Assert.Equal(AssetLifecycle.Draft, asset.GetProperty(AssetFields.Lifecycle).GetString());
        Assert.Equal(AssetsSeed.Laptop, asset.GetProperty(AssetFields.AssetTypeId).GetGuid());
        Assert.Equal(asset.ToString(), (await Get(id)).ToString());
        await using (var db = await context.Database())
        {
            var row = await db.Assets.SingleAsync(row => row.Id == id);
            Assert.Equal(AssetLifecycle.Draft, row.Lifecycle);
            Assert.Equal(AssetStatusCodes.InStock, (await db.Statuses.SingleAsync(status => status.Id == row.StatusId)).Code);
        }
        using var restarted = context.Host.WithWebHostBuilder(_ => { });
        using var client = restarted.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, AssetsTestContext.Path(id: id));
        request.Headers.Authorization = new("Bearer", context.Full);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(asset.ToString(), (await Body(response)).ToString());
    }

    [Theory]
    [InlineData(AssetResources.Type)]
    [InlineData(AssetResources.Manufacturer)]
    [InlineData(AssetResources.Model)]
    [InlineData(AssetResources.Location)]
    [InlineData(AssetResources.Status)]
    public async Task AllMasterCrudUsesGenericResourceRoutes(string resource)
    {
        var values = new Dictionary<string, object> { [AssetFields.Code] = Guid.NewGuid().ToString("N")[..AssetLimits.CodeLength], [AssetFields.Name] = "Catalogue entry" };
        if (resource == AssetResources.Model) values.Add(AssetFields.ManufacturerId, AssetsSeed.Dell);
        var master = await context.Create(resource, values); var id = Id(master);
        Assert.True(master.GetProperty(AssetFields.Active).GetBoolean());
        using var patch = await context.Send(HttpMethod.Patch, resource, id, new { name = "Edited", active = false });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        using var read = await context.Send(HttpMethod.Get, resource, id);
        Assert.Equal("Edited", (await Body(read)).GetProperty(AssetFields.Name).GetString());
        using var list = await context.Raw(HttpMethod.Get, AssetsTestContext.Path(resource) + "?filter=" + Filter(AssetFields.Id, id), null, context.Reader);
        Assert.Equal(1, (await Body(list)).GetProperty("total").GetInt32());
        using var deleted = await context.Send(HttpMethod.Delete, resource, id);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        await Error(await context.Send(HttpMethod.Get, resource, id), HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"name\":\"No type\"}")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{\"name\":true}")]
    [InlineData("{\"name\":\"A\",\"assetTypeId\":\"invalid\"}")]
    public async Task InvalidCreateRemainsBadRequest(string json)
        => await Error(await context.Raw(HttpMethod.Post, AssetsTestContext.Path(), json, context.Full), HttpStatusCode.BadRequest);

    [Theory]
    [InlineData(byte.MinValue - 1, false)]
    [InlineData(byte.MinValue, true)]
    [InlineData(50, true)]
    [InlineData(MaximumCondition, true)]
    [InlineData(MaximumCondition + 1, false)]
    [InlineData(byte.MaxValue, false)]
    [InlineData(byte.MaxValue + 1, false)]
    public async Task ConditionUsesRuntimeIntrinsicTypeAndDomainConstraint(int condition, bool valid)
    {
        using var created = await context.Send(HttpMethod.Post, AssetResources.Asset, body: new { name = "Condition", assetTypeId = AssetsSeed.Laptop, condition });
        Assert.Equal(valid ? HttpStatusCode.Created : HttpStatusCode.BadRequest, created.StatusCode);
        var asset = await context.Asset(); var id = Id(asset);
        using var patched = await context.Send(HttpMethod.Patch, AssetResources.Asset, id, new { condition });
        Assert.Equal(valid ? HttpStatusCode.OK : HttpStatusCode.BadRequest, patched.StatusCode);
        if (!valid) Assert.Equal(asset.ToString(), (await Get(id)).ToString());
    }

    [Fact]
    public async Task ImmutableTypeAndProtectedStateHaveNoPayloadBypass()
    {
        var asset = await context.Asset(); var id = Id(asset);
        var other = await context.Create(AssetResources.Type, new { code = Guid.NewGuid().ToString("N")[..AssetLimits.CodeLength], name = "Other type" });
        await Error(await context.Send(HttpMethod.Patch, AssetResources.Asset, id, new { assetTypeId = Id(other) }), HttpStatusCode.Conflict);
        await Error(await context.Send(HttpMethod.Patch, AssetResources.Asset, id, new { lifecycle = AssetLifecycle.Ready }), HttpStatusCode.BadRequest);
        await Error(await context.Send(HttpMethod.Patch, AssetResources.Asset, id, new { statusId = Guid.NewGuid() }), HttpStatusCode.BadRequest);
        await Error(await context.Send(HttpMethod.Patch, AssetResources.Asset, id, new { createdAt = DateTimeOffset.UtcNow }), HttpStatusCode.BadRequest);
        await Error(await context.Action(id, AssetActions.Complete, parameters: new { assetTypeId = Id(other) }), HttpStatusCode.BadRequest);
        Assert.Equal(asset.ToString(), (await Get(id)).ToString());
        using var rename = await context.Send(HttpMethod.Patch, AssetResources.Asset, id, new { name = "Renamed", purchaseDate = "2026-10-01", purchasePrice = 123.45m });
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
        var updated = await Get(id);
        Assert.Equal("Renamed", updated.GetProperty(AssetFields.Name).GetString());
        Assert.Equal(AssetsSeed.Laptop, updated.GetProperty(AssetFields.AssetTypeId).GetGuid());
        Assert.Equal(123.45m, updated.GetProperty(AssetFields.PurchasePrice).GetDecimal());
    }

    [Fact]
    public async Task RelationsRequireExistingCoherentActiveReferencesAndProtectDeletes()
    {
        var asset = await context.Asset(); var id = Id(asset);
        await Error(await context.Send(HttpMethod.Post, AssetResources.Asset, body: new { name = "Missing type", assetTypeId = Guid.NewGuid() }), HttpStatusCode.NotFound);
        await Error(await context.Send(HttpMethod.Patch, AssetResources.Asset, id, new { locationId = Guid.NewGuid() }), HttpStatusCode.NotFound);
        await Error(await context.Send(HttpMethod.Patch, AssetResources.Asset, id, new { manufacturerId = Guid.NewGuid() }), HttpStatusCode.NotFound);
        await Error(await context.Send(HttpMethod.Patch, AssetResources.Asset, id, new { assetModelId = Guid.NewGuid() }), HttpStatusCode.NotFound);
        await Error(await context.Send(HttpMethod.Patch, AssetResources.Asset, id, new { assetModelId = AssetsSeed.Latitude }), HttpStatusCode.Conflict);
        var manufacturer = await context.Create(AssetResources.Manufacturer, new { code = Guid.NewGuid().ToString("N")[..AssetLimits.CodeLength], name = "Other manufacturer" });
        await Error(await context.Send(HttpMethod.Patch, AssetResources.Asset, id, new { manufacturerId = Id(manufacturer), assetModelId = AssetsSeed.Latitude }), HttpStatusCode.Conflict);
        Assert.Equal(asset.ToString(), (await Get(id)).ToString());
        using var valid = await context.Send(HttpMethod.Patch, AssetResources.Asset, id, new { manufacturerId = AssetsSeed.Dell, assetModelId = AssetsSeed.Latitude, locationId = AssetsSeed.Office });
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        await Error(await context.Send(HttpMethod.Patch, AssetResources.Asset, id, new { manufacturerId = (Guid?)null }), HttpStatusCode.Conflict);
        await Error(await context.Send(HttpMethod.Delete, AssetResources.Type, AssetsSeed.Laptop), HttpStatusCode.Conflict);
        await Error(await context.Send(HttpMethod.Delete, AssetResources.Manufacturer, AssetsSeed.Dell), HttpStatusCode.Conflict);
        await Error(await context.Send(HttpMethod.Delete, AssetResources.Model, AssetsSeed.Latitude), HttpStatusCode.Conflict);
        await Error(await context.Send(HttpMethod.Delete, AssetResources.Location, AssetsSeed.Office), HttpStatusCode.Conflict);
        await Error(await context.Send(HttpMethod.Post, AssetResources.Model, body: new { code = "missing", name = "Missing", manufacturerId = Guid.NewGuid() }), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ActiveReferenceRulesApplyToNewSelectionsAndAllowClearingExistingOnes()
    {
        var manufacturer = await context.Create(AssetResources.Manufacturer, new { code = Guid.NewGuid().ToString("N")[..AssetLimits.CodeLength], name = "Manufacturer" });
        var model = await context.Create(AssetResources.Model, new { code = Guid.NewGuid().ToString("N")[..AssetLimits.CodeLength], name = "Model", manufacturerId = Id(manufacturer) });
        var location = await context.Create(AssetResources.Location, new { code = Guid.NewGuid().ToString("N")[..AssetLimits.CodeLength], name = "Location" });
        var asset = await context.Create(AssetResources.Asset, new
        {
            name = "Existing references",
            assetTypeId = AssetsSeed.Laptop,
            manufacturerId = Id(manufacturer),
            assetModelId = Id(model),
            locationId = Id(location)
        });
        foreach (var target in new[] { (AssetResources.Manufacturer, Id(manufacturer)), (AssetResources.Model, Id(model)), (AssetResources.Location, Id(location)) })
        {
            using var inactive = await context.Send(HttpMethod.Patch, target.Item1, target.Item2, new { active = false });
            Assert.Equal(HttpStatusCode.OK, inactive.StatusCode);
        }
        using var rename = await context.Send(HttpMethod.Patch, AssetResources.Asset, Id(asset), new { name = "Retained references" });
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
        using var complete = await context.Action(Id(asset), AssetActions.Complete); Assert.Equal(HttpStatusCode.NoContent, complete.StatusCode);
        await Error(await context.Send(HttpMethod.Post, AssetResources.Asset, body: new { name = "New inactive manufacturer", assetTypeId = AssetsSeed.Laptop, manufacturerId = Id(manufacturer) }), HttpStatusCode.Conflict);
        await Error(await context.Send(HttpMethod.Post, AssetResources.Asset, body: new { name = "New inactive location", assetTypeId = AssetsSeed.Laptop, locationId = Id(location) }), HttpStatusCode.Conflict);
        using var cleared = await context.Send(HttpMethod.Patch, AssetResources.Asset, Id(asset), new { manufacturerId = (Guid?)null, assetModelId = (Guid?)null, locationId = (Guid?)null });
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        using var deleteModel = await context.Send(HttpMethod.Delete, AssetResources.Model, Id(model)); Assert.Equal(HttpStatusCode.NoContent, deleteModel.StatusCode);
        using var deleteManufacturer = await context.Send(HttpMethod.Delete, AssetResources.Manufacturer, Id(manufacturer)); Assert.Equal(HttpStatusCode.NoContent, deleteManufacturer.StatusCode);
    }

    [Fact]
    public async Task NameIsRequiredEvenWhenTypeIsValid()
    {
        await Error(await context.Send(HttpMethod.Post, AssetResources.Asset, body: new { assetTypeId = AssetsSeed.Laptop }), HttpStatusCode.BadRequest);
        await Error(await context.Send(HttpMethod.Post, AssetResources.Asset, body: new { name = "   ", assetTypeId = AssetsSeed.Laptop }), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task InactiveTypesBlockNewAssetsButNotExistingCompletion()
    {
        var type = await context.Create(AssetResources.Type, new { code = Guid.NewGuid().ToString("N")[..AssetLimits.CodeLength], name = "Configurable" });
        var asset = await context.Create(AssetResources.Asset, new { name = "Before deactivation", assetTypeId = Id(type) });
        using var disabled = await context.Send(HttpMethod.Patch, AssetResources.Type, Id(type), new { active = false });
        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        await Error(await context.Send(HttpMethod.Post, AssetResources.Asset, body: new { name = "After deactivation", assetTypeId = Id(type) }), HttpStatusCode.Conflict);
        using var complete = await context.Action(Id(asset), AssetActions.Complete); Assert.Equal(HttpStatusCode.NoContent, complete.StatusCode);
    }

    [Fact]
    public async Task CompleteAndRetireAreAtomicIrreversibleRuntimeActions()
    {
        var asset = await context.Asset(); var id = Id(asset);
        await Error(await context.Action(id, AssetActions.Retire), HttpStatusCode.Conflict);
        using var complete = await context.Action(id, AssetActions.Complete, context.Writer);
        Assert.Equal(HttpStatusCode.NoContent, complete.StatusCode);
        Assert.Equal(AssetLifecycle.Ready, (await Get(id)).GetProperty(AssetFields.Lifecycle).GetString());
        await Error(await context.Action(id, AssetActions.Complete), HttpStatusCode.Conflict);
        using var retire = await context.Action(id, AssetActions.Retire);
        Assert.Equal(HttpStatusCode.NoContent, retire.StatusCode);
        var retired = await Get(id); Assert.Equal(AssetLifecycle.Retired, retired.GetProperty(AssetFields.Lifecycle).GetString());
        await Error(await context.Send(HttpMethod.Patch, AssetResources.Asset, id, new { name = "Bypass" }), HttpStatusCode.Conflict);
        await Error(await context.Send(HttpMethod.Delete, AssetResources.Asset, id), HttpStatusCode.Conflict);
        await Error(await context.Action(id, AssetActions.Complete), HttpStatusCode.Conflict);
        await Error(await context.Action(id, AssetActions.Retire), HttpStatusCode.Conflict);
        await Error(await context.Action(id, "reactivate"), HttpStatusCode.NotFound);
        await Error(await context.Send(HttpMethod.Patch, AssetResources.Asset, id, new { lifecycle = AssetLifecycle.Draft }), HttpStatusCode.BadRequest);
        Assert.Equal(retired.ToString(), (await Get(id)).ToString());
        await using var db = await context.Database();
        Assert.Equal(AssetStatusCodes.Retired, (await db.Statuses.SingleAsync(status => status.Id == retired.GetProperty(AssetFields.StatusId).GetGuid())).Code);
    }

    [Fact]
    public async Task PermissionsAreSeparatedAndTenantMembershipIsRequired()
    {
        var asset = await context.Asset(); var id = Id(asset);
        await Error(await context.Raw(HttpMethod.Get, AssetsTestContext.Path(id: id), null, null), HttpStatusCode.Unauthorized);
        await Error(await context.Send(HttpMethod.Get, AssetResources.Asset, id, token: context.NoPermission), HttpStatusCode.Forbidden);
        foreach (string token in new[] { context.Reader, context.Schema, context.MaintainWithoutRead, context.NoTenant })
            await Error(await context.Send(HttpMethod.Post, AssetResources.Asset, body: new { name = "Denied", assetTypeId = AssetsSeed.Laptop }, token: token), HttpStatusCode.Forbidden);
        await Error(await context.Send(HttpMethod.Post, AssetResources.Type, body: new { code = "DENIED", name = "Denied" }, token: context.Writer), HttpStatusCode.Forbidden);
        var type = await context.Create(AssetResources.Type, new { code = Guid.NewGuid().ToString("N")[..AssetLimits.CodeLength], name = "Schema permission" }, context.Schema);
        Assert.NotEqual(Guid.Empty, Id(type));
        await Error(await context.Action(id, AssetActions.Complete, context.Schema), HttpStatusCode.Forbidden);
        using var ready = await context.Action(id, AssetActions.Complete, context.Writer); Assert.Equal(HttpStatusCode.NoContent, ready.StatusCode);
        await Error(await context.Action(id, AssetActions.Retire, context.Writer), HttpStatusCode.Forbidden);
        await Error(await context.Action(id, AssetActions.Retire, context.SensitiveWithoutMaintain), HttpStatusCode.Forbidden);
        using var retire = await context.Action(id, AssetActions.Retire, context.Full); Assert.Equal(HttpStatusCode.NoContent, retire.StatusCode);
    }

    [Fact]
    public async Task TenantDatabasesAreIndependentAndSpoofedSelectorCannotCrossThem()
    {
        var asset = await context.Asset(token: context.OtherTenant); var id = Id(asset);
        await Error(await context.Send(HttpMethod.Get, AssetResources.Asset, id), HttpStatusCode.NotFound);
        Assert.Equal(id, Id(await Get(id, context.OtherTenant)));
        await using var primary = await context.Database(); await using var secondary = await context.Database(true);
        Assert.False(await primary.Assets.AnyAsync(row => row.Id == id)); Assert.True(await secondary.Assets.AnyAsync(row => row.Id == id));
        using var request = new HttpRequestMessage(HttpMethod.Get, AssetsTestContext.Path(id: id));
        request.Headers.Authorization = new("Bearer", context.Full); request.Headers.Add("X-Tenant-Id", "assets-foreign");
        using var response = await context.Client.SendAsync(request); Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RuntimeCanExecuteThePersistentSliceWithoutHttpCrud()
    {
        await using var scope = context.Host.Services.CreateAsyncScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, context.FullUser.ToString())], "fixture"))
        };
        try
        {
            var runtime = scope.ServiceProvider.GetRequiredService<ResourceRuntime>();
            var values = new ResourceRecord(new Dictionary<string, ScalarValue> { [AssetFields.Name] = new TextValue("Direct runtime"), [AssetFields.AssetTypeId] = new UuidValue(AssetsSeed.Laptop) });
            var row = await runtime.CreateAsync(AssetResources.Asset, values);
            var id = ((UuidValue)row.Values[AssetFields.Id]).Value; context.Track(AssetResources.Asset, id);
            var identifier = new ResourceIdentifier(AssetFields.Id, new UuidValue(id));
            var actions = scope.ServiceProvider.GetRequiredService<ResourceActionRuntime>();
            await actions.ExecuteAsync(AssetResources.Asset, AssetActions.Complete, new ResourceEntityTarget(identifier), new());
            Assert.Equal(new TextValue(AssetLifecycle.Ready), (await runtime.GetAsync(AssetResources.Asset, identifier)).Values[AssetFields.Lifecycle]);
            await actions.ExecuteAsync(AssetResources.Asset, AssetActions.Retire, new ResourceEntityTarget(identifier), new());
            var failure = await Assert.ThrowsAsync<ResourceRuntimeException>(() => runtime.UpdateAsync(AssetResources.Asset, identifier,
                new ResourceRecord(new Dictionary<string, ScalarValue> { [AssetFields.Name] = new TextValue("Denied") })));
            Assert.Equal(ResourceRuntimeError.Conflict, failure.Code); Assert.Equal(AssetFailures.Retired, failure.ReasonCode);
        }
        finally { accessor.HttpContext = null; }
    }

    [Fact]
    public async Task DatabaseConstraintsProtectReferencesAndUniquenessIndependently()
    {
        await using var db = await context.Database();
        db.Manufacturers.Remove(await db.Manufacturers.SingleAsync(row => row.Id == AssetsSeed.Dell));
        var reference = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(reference.InnerException).SqlState);
        db.ChangeTracker.Clear();
        var original = await db.Types.SingleAsync(row => row.Id == AssetsSeed.Laptop);
        db.Types.Add(new AssetTypeRow { Id = Guid.NewGuid(), Code = original.Code, Name = "Duplicate", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
        var unique = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(unique.InnerException).SqlState);
    }

    [Fact]
    public async Task FilteringCombinesPredicatesBeforePaginationAndTotals()
    {
        var first = await context.Asset(); var second = await context.Asset();
        foreach (var asset in new[] { first, second })
        {
            using var patch = await context.Send(HttpMethod.Patch, AssetResources.Asset, Id(asset), new { locationId = AssetsSeed.Office });
            Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        }
        using var complete = await context.Action(Id(second), AssetActions.Complete); Assert.Equal(HttpStatusCode.NoContent, complete.StatusCode);
        string path = AssetsTestContext.Path() + "?limit=1&sort=name&filter=" + Filter(AssetFields.AssetTypeId, AssetsSeed.Laptop)
            + "&filter=" + Filter(AssetFields.LocationId, AssetsSeed.Office) + "&filter=" + Filter(AssetFields.Lifecycle, AssetLifecycle.Ready)
            + "&filter=" + Filter(AssetFields.Id, Id(second)) + "&filter=" + Filter(AssetFields.StatusId, second.GetProperty(AssetFields.StatusId).GetGuid());
        using var response = await context.Raw(HttpMethod.Get, path, null, context.Reader);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); var page = await Body(response);
        Assert.Equal(1, page.GetProperty("total").GetInt32()); Assert.Equal(Id(second), Id(Assert.Single(page.GetProperty("items").EnumerateArray())));
        await Error(await context.Raw(HttpMethod.Get, AssetsTestContext.Path() + "?filter=" + Filter("unknown", "value"), null, context.Reader), HttpStatusCode.BadRequest);
    }
    private static string Filter(string field, object value) => Uri.EscapeDataString(JsonSerializer.Serialize(new { field, @operator = "eq", value }));

    [Fact]
    public async Task ConcurrentMutationsCannotBypassUniquenessTransitionsOrReferenceIntegrity()
    {
        string tag = "TAG-" + Guid.NewGuid();
        var creates = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => context.Send(HttpMethod.Post, AssetResources.Asset,
            body: new { name = "Concurrent", assetTypeId = AssetsSeed.Laptop, assetTag = tag })));
        try { Assert.Equal(new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }, creates.Select(response => response.StatusCode).OrderBy(status => (int)status)); }
        finally { foreach (var response in creates) response.Dispose(); }
        var asset = await context.Asset(); var id = Id(asset);
        var completes = await Task.WhenAll(context.Action(id, AssetActions.Complete), context.Action(id, AssetActions.Complete));
        try { Assert.Equal(new[] { HttpStatusCode.NoContent, HttpStatusCode.Conflict }, completes.Select(response => response.StatusCode).OrderBy(status => (int)status)); }
        finally { foreach (var response in completes) response.Dispose(); }
        var writes = await Task.WhenAll(context.Action(id, AssetActions.Retire), context.Send(HttpMethod.Patch, AssetResources.Asset, id, new { name = "Before retirement" }));
        try
        {
            Assert.Equal(HttpStatusCode.NoContent, writes[0].StatusCode); Assert.Contains(writes[1].StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
            var final = await Get(id); Assert.Equal(AssetLifecycle.Retired, final.GetProperty(AssetFields.Lifecycle).GetString());
            Assert.Equal(writes[1].StatusCode == HttpStatusCode.OK ? "Before retirement" : asset.GetProperty(AssetFields.Name).GetString(), final.GetProperty(AssetFields.Name).GetString());
        }
        finally { foreach (var response in writes) response.Dispose(); }
        var type = await context.Create(AssetResources.Type, new { code = Guid.NewGuid().ToString("N")[..AssetLimits.CodeLength], name = "Race" });
        var race = await Task.WhenAll(context.Send(HttpMethod.Delete, AssetResources.Type, Id(type)),
            context.Send(HttpMethod.Post, AssetResources.Asset, body: new { name = "Reference race", assetTypeId = Id(type) }));
        try
        {
            Assert.True((race[0].StatusCode == HttpStatusCode.NoContent && race[1].StatusCode == HttpStatusCode.NotFound)
                || (race[0].StatusCode == HttpStatusCode.Conflict && race[1].StatusCode == HttpStatusCode.Created));
        }
        finally { foreach (var response in race) response.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EligibleDraftAndReadyAssetsCanBeDeletedThroughRuntime(bool ready)
    {
        var asset = await context.Asset(); var id = Id(asset);
        if (ready) { using var complete = await context.Action(id, AssetActions.Complete); Assert.Equal(HttpStatusCode.NoContent, complete.StatusCode); }
        using var deleted = await context.Send(HttpMethod.Delete, AssetResources.Asset, id);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        await Error(await context.Send(HttpMethod.Get, AssetResources.Asset, id), HttpStatusCode.NotFound);
        await Error(await context.Send(HttpMethod.Delete, AssetResources.Asset, id), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CompletionValidatesPersistedStaticConstraintsAndAssignedStatusBlocksRetirement()
    {
        var asset = await context.Asset(); var id = Id(asset);
        await using (var db = await context.Database())
        {
            var row = await db.Assets.SingleAsync(row => row.Id == id);
            row.PurchasePrice = -1; await db.SaveChangesAsync();
        }
        await Error(await context.Action(id, AssetActions.Complete), HttpStatusCode.Conflict);
        await using (var db = await context.Database()) Assert.Equal(AssetLifecycle.Draft, (await db.Assets.SingleAsync(row => row.Id == id)).Lifecycle);
        // Privileged fixture repairs legacy invalid data; production input rejects negative prices.
        await using (var db = await context.Database())
        {
            var row = await db.Assets.SingleAsync(row => row.Id == id); row.PurchasePrice = null; await db.SaveChangesAsync();
        }
        using var complete = await context.Action(id, AssetActions.Complete); Assert.Equal(HttpStatusCode.NoContent, complete.StatusCode);
        await using (var db = await context.Database())
        {
            var row = await db.Assets.SingleAsync(row => row.Id == id);
            row.StatusId = (await db.Statuses.SingleAsync(status => status.Code == AssetStatusCodes.Assigned)).Id;
            await db.SaveChangesAsync();
        }
        await Error(await context.Action(id, AssetActions.Retire), HttpStatusCode.Conflict);
        Assert.Equal(AssetLifecycle.Ready, (await Get(id)).GetProperty(AssetFields.Lifecycle).GetString());
    }

    [Fact]
    public async Task ModuleGateBlocksExecutionAndReenablePreservesData()
    {
        var asset = await context.Asset(); var id = Id(asset);
        var modules = context.Host.Services.GetRequiredService<ModuleStateService>();
        try
        {
            await modules.DisableAsync(AssetsModule.Id);
            await Error(await context.Send(HttpMethod.Get, AssetResources.Asset, id), HttpStatusCode.ServiceUnavailable);
            await Error(await context.Action(id, AssetActions.Complete), HttpStatusCode.ServiceUnavailable);
        }
        finally { await modules.EnableAsync(AssetsModule.Id); }
        Assert.Equal(asset.ToString(), (await Get(id)).ToString());
    }

    [Fact]
    public async Task MissingActionTargetAndInvalidArgumentsUseExistingRuntimeErrors()
    {
        await Error(await context.Action(Guid.NewGuid(), AssetActions.Complete), HttpStatusCode.NotFound);
        var asset = await context.Asset(); var id = Id(asset);
        await Error(await context.Action(id, "unknown"), HttpStatusCode.NotFound);
        await Error(await context.Action(id, AssetActions.Complete, parameters: new { unexpected = true }), HttpStatusCode.BadRequest);
        await Error(await context.Send(HttpMethod.Patch, AssetResources.Asset, id, new { name = "   " }), HttpStatusCode.BadRequest);
        await Error(await context.Send(HttpMethod.Patch, AssetResources.Asset, id, new { purchasePrice = -1 }), HttpStatusCode.BadRequest);
        await Error(await context.Send(HttpMethod.Patch, AssetResources.Asset, id, new { purchaseDate = "2026-02-30" }), HttpStatusCode.BadRequest);
        Assert.Equal(asset.ToString(), (await Get(id)).ToString());
    }

    [Fact]
    public async Task ReservedStatusesCannotBeDeletedDeactivatedOrRecoded()
    {
        await using var db = await context.Database();
        var status = await db.Statuses.SingleAsync(row => row.Code == AssetStatusCodes.InStock);
        await Error(await context.Send(HttpMethod.Delete, AssetResources.Status, status.Id), HttpStatusCode.Conflict);
        await Error(await context.Send(HttpMethod.Patch, AssetResources.Status, status.Id, new { active = false }), HttpStatusCode.Conflict);
        await Error(await context.Send(HttpMethod.Patch, AssetResources.Status, status.Id, new { code = "OTHER" }), HttpStatusCode.Conflict);
    }
}
