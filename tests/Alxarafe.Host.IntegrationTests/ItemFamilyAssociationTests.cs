using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Alxarafe.Modularity;
using Alxarafe.Modules.Wms;
using Alxarafe.Modules.Wms.Application;
using Alxarafe.Modules.Wms.Http;
using Alxarafe.Modules.Wms.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed partial class ItemFamilyTests
{
    private static readonly string[] AssociationFields = ["itemFamilyId", "storageAttribute"];
    private static readonly string[] ConflictCodes = [ItemFamilyAssociationEndpoints.AlreadyAssociated, ItemFamilyAssociationEndpoints.NotAssociated];
    private static readonly string[] ConflictPaths = ["/add/0", "/remove/0"];
    private static readonly string[] MissingPaths = ["/add/1", "/remove/0"];
    private const string AttributeSuffix = ItemFamilyAssociationEndpoints.AttributePathSuffix;
    private const string StorageAttributeMigration = "202610150001_StorageAttributes";
    private const string ItemFamilyMigration = "202610160001_ItemFamilies";

    private async Task<(string Path, Guid[] Attributes)> PrepareAssociations()
    {
        var family = (await catalogue.CreateAsync(Each, "Family")).ItemFamily!;
        var attributes = factory.Services.GetRequiredService<StorageAttributeCatalogue>();
        var ids = new List<Guid>();
        foreach (string code in new[] { "IF_A1", "IF_A10", "IF_A2" })
        {
            var attribute = (await attributes.CreateAsync(code, " 	" + code + "\r\n")).StorageAttribute!;
            ids.Add(attribute.Id);
            ownedAttributes.Add(attribute.Id);
        }
        return (ItemFamilyEndpoints.Path + "/" + family.Id + AttributeSuffix, ids.ToArray());
    }

    private async Task AssertAssociated(string path, params Guid[] ids)
    {
        var response = await Send(HttpMethod.Get, path);
        Headers(response, HttpStatusCode.OK);
        var page = await Body(response);
        Assert.Equal(ids.Length, page.GetProperty("total").GetInt32());
        Assert.Equal(ids, page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()));
    }

    private static void NoContent(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Empty(response.Headers.WwwAuthenticate);
        Assert.Null(response.Headers.RetryAfter);
    }

    [Fact]
    public async Task ConcurrentHttpCreationOfCodeAndPairHasOneWinnerAndNoDuplicates()
    {
        var creations = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Send(HttpMethod.Post, ItemFamilyEndpoints.Path,
            JsonSerializer.Serialize(new { code = Race, name = "Family" }))));
        Assert.Single(creations, response => response.StatusCode == HttpStatusCode.Created);
        await Error(Assert.Single(creations, response => response.StatusCode == HttpStatusCode.Conflict), HttpStatusCode.Conflict, ItemFamilyEndpoints.CodeConflict);
        var (path, attributes) = await PrepareAssociations();
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Send(HttpMethod.Post, path,
            JsonSerializer.Serialize(new { storageAttributeId = attributes[0] }))));
        var success = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Headers(success, HttpStatusCode.Created);
        Assert.Equal(path + "/" + attributes[0], success.Headers.Location!.OriginalString);
        var body = await Body(success);
        Assert.Equal(AssociationFields, body.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(attributes[0], body.GetProperty("storageAttribute").GetProperty("id").GetGuid());
        var error = await Body(Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(ItemFamilyAssociationEndpoints.CreateConflict, error.GetProperty("code").GetString());
        Assert.Equal(ItemFamilyAssociationEndpoints.AlreadyAssociated, error.GetProperty("errors")[0].GetProperty("code").GetString());
        await using var context = await contexts.CreateDbContextAsync();
        Assert.Single(await context.ItemFamilies.Where(row => row.Code == Race).ToArrayAsync());
        Assert.Single(await context.ItemFamilyAssociations.ToArrayAsync());
        await AssertAssociated(path, attributes[0]);
    }

    [Fact]
    public async Task ReplaceIsExactIdempotentEmptyAndAtomicOnMissingReferences()
    {
        var (path, ids) = await PrepareAssociations();
        async Task<HttpResponseMessage> Put(params Guid[] values) => await Send(HttpMethod.Put, path, JsonSerializer.Serialize(new { storageAttributeIds = values }));
        NoContent(await Put(ids[1], ids[0]));
        NoContent(await Put(ids[0], ids[1]));
        await AssertAssociated(path, ids[0], ids[1]);
        Guid missing = Guid.NewGuid();
        var rejected = await Put(ids[2], missing);
        Headers(rejected, HttpStatusCode.NotFound);
        var error = await Body(rejected);
        Assert.Equal(ItemFamilyAssociationEndpoints.AttributeMissing, error.GetProperty("code").GetString());
        Assert.Equal("/storageAttributeIds/1", error.GetProperty("errors")[0].GetProperty("path").GetString());
        await AssertAssociated(path, ids[0], ids[1]);
        await Error(await Put(ids[0], ids[0]), HttpStatusCode.BadRequest, ItemFamilyEndpoints.InvalidRequest);
        await AssertAssociated(path, ids[0], ids[1]);
        NoContent(await Put());
        await AssertAssociated(path);
        await using var context = await contexts.CreateDbContextAsync();
        Assert.Equal(ids.Length, await context.StorageAttributes.CountAsync(row => ids.Contains(row.Id)));
    }

    [Fact]
    public async Task PatchCollectsAllPrestateConflictsAndChecksExistenceBeforeAnyConflict()
    {
        var (path, ids) = await PrepareAssociations();
        NoContent(await Send(HttpMethod.Put, path, JsonSerializer.Serialize(new { storageAttributeIds = new[] { ids[0] } })));
        async Task<HttpResponseMessage> Patch(Guid[] add, Guid[] remove) => await Send(HttpMethod.Patch, path, JsonSerializer.Serialize(new { add, remove }));
        var response = await Patch([ids[0], ids[1]], [ids[2]]);
        Headers(response, HttpStatusCode.Conflict);
        var error = await Body(response);
        Assert.Equal(ItemFamilyAssociationEndpoints.ChangeConflict, error.GetProperty("code").GetString());
        var details = error.GetProperty("errors").EnumerateArray().ToArray();
        Assert.Equal(ConflictCodes, details.Select(detail => detail.GetProperty("code").GetString()));
        Assert.Equal(ConflictPaths, details.Select(detail => detail.GetProperty("path").GetString()));
        Assert.All(details, detail => Assert.Equal(ItemFamilyAssociationEndpoints.AttributeResource, detail.GetProperty("resource").GetString()));
        await AssertAssociated(path, ids[0]);
        Guid missing = Guid.NewGuid();
        response = await Patch([ids[0], missing], [missing]);
        Headers(response, HttpStatusCode.NotFound);
        error = await Body(response);
        Assert.Equal(ItemFamilyAssociationEndpoints.MultipleMissing, error.GetProperty("code").GetString());
        Assert.Equal(MissingPaths, error.GetProperty("errors").EnumerateArray().Select(detail => detail.GetProperty("path").GetString()));
        await AssertAssociated(path, ids[0]);
        response = await Patch([ids[0]], [ids[0]]);
        Headers(response, HttpStatusCode.Conflict);
        Assert.Equal(ItemFamilyAssociationEndpoints.AlreadyAssociated, (await Body(response)).GetProperty("errors")[0].GetProperty("code").GetString());
        await AssertAssociated(path, ids[0]);
        NoContent(await Patch([ids[1], ids[2]], [ids[0]]));
        await AssertAssociated(path, ids[1], ids[2]);
        NoContent(await Patch([], [ids[2]]));
        await AssertAssociated(path, ids[1]);
    }

    [Fact]
    public async Task DeleteDistinguishesMissingFamilyMasterAndPairAndPreservesMasters()
    {
        var (path, ids) = await PrepareAssociations();
        NoContent(await Send(HttpMethod.Put, path, JsonSerializer.Serialize(new { storageAttributeIds = new[] { ids[0] } })));
        var deleted = await Send(HttpMethod.Delete, path + "/" + ids[0]);
        NoContent(deleted);
        Assert.Equal("", await deleted.Content.ReadAsStringAsync());
        foreach (var (url, code, resource) in new[]
        {
            (path + "/" + ids[0], ItemFamilyAssociationEndpoints.AssociationMissing, ItemFamilyAssociationEndpoints.AssociationResource),
            (path + "/" + Guid.NewGuid(), ItemFamilyAssociationEndpoints.AttributeMissing, ItemFamilyAssociationEndpoints.AttributeResource),
            (ItemFamilyEndpoints.Path + "/" + Guid.NewGuid() + AttributeSuffix + "/" + ids[0], ItemFamilyAssociationEndpoints.FamilyMissing, ItemFamilyAssociationEndpoints.FamilyResource)
        })
        {
            var response = await Send(HttpMethod.Delete, url);
            Headers(response, HttpStatusCode.NotFound);
            var body = await Body(response);
            Assert.Equal(code, body.GetProperty("code").GetString());
            var detail = Assert.Single(body.GetProperty("errors").EnumerateArray());
            Assert.Equal(resource, detail.GetProperty("resource").GetString());
            Assert.False(detail.TryGetProperty("path", out _));
        }
        await AssertAssociated(path);
        await using var context = await contexts.CreateDbContextAsync();
        Assert.Equal(ids.Length, await context.StorageAttributes.CountAsync(row => ids.Contains(row.Id)));
        Assert.Single(await context.ItemFamilies.ToArrayAsync());
    }

    [Fact]
    public async Task AllAssociationOperationsEnforceIdentityAdminAndDynamicGateBeforeInvalidInput()
    {
        var (path, ids) = await PrepareAssociations();
        NoContent(await Send(HttpMethod.Put, path, JsonSerializer.Serialize(new { storageAttributeIds = ids })));
        foreach (bool enabled in new[] { true, false })
        {
            await Toggle(enabled);
            foreach (var method in new[] { HttpMethod.Get, HttpMethod.Post, HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete })
            {
                string url = method == HttpMethod.Delete ? path + "/invalid" : path + "?unknown=1";
                foreach (string token in new[] { "", userToken })
                    await Error(await Send(method, url, method == HttpMethod.Get ? null : "{", token),
                        token.Length == 0 ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, token.Length == 0 ? "unauthorized" : "forbidden");
                await Error(await Send(method, url, method == HttpMethod.Get ? null : "{"),
                    enabled ? HttpStatusCode.BadRequest : HttpStatusCode.ServiceUnavailable,
                    enabled ? ItemFamilyEndpoints.InvalidRequest : ModuleDisabledResponse.ErrorCode);
            }
        }
        await Toggle(true);
        await AssertAssociated(path, ids);
        foreach (string locale in new[] { "es", "en" })
        {
            var response = await Send(HttpMethod.Get, path, locale: locale);
            Headers(response, HttpStatusCode.OK);
            Assert.Equal("IF_A1", (await Body(response)).GetProperty("items")[0].GetProperty("name").GetString());
        }
    }

    [Fact]
    public async Task AssociationStructureAndQueriesAreStrictAndPageOrderIsOrdinal()
    {
        var (path, ids) = await PrepareAssociations();
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Patch })
            foreach (string body in new[] { "{", "null", "[]", "{}", "{\"extra\":true}", "{\"add\":[],\"remove\":[]}", "{\"storageAttributeId\":null}" })
                await Error(await Send(method, path, body), HttpStatusCode.BadRequest, ItemFamilyEndpoints.InvalidRequest);
        foreach (string body in new[]
        {
            JsonSerializer.Serialize(new { add = new[] { ids[0], ids[0] } }),
            JsonSerializer.Serialize(new { remove = new[] { ids[0], ids[0] } }),
            "{\"add\":[],\"add\":[]}", "{\"Add\":[]}", "{\"add\":null}"
        }) await Error(await Send(HttpMethod.Patch, path, body), HttpStatusCode.BadRequest, ItemFamilyEndpoints.InvalidRequest);
        foreach (string badId in new[] { Guid.Empty.ToString(), ids[0].ToString().ToUpperInvariant(), "bad", ids[0].ToString("N") })
            await Error(await Send(HttpMethod.Post, path, JsonSerializer.Serialize(new { storageAttributeId = badId })), HttpStatusCode.BadRequest, ItemFamilyEndpoints.InvalidRequest);
        await AssertAssociated(path);
        NoContent(await Send(HttpMethod.Put, path, JsonSerializer.Serialize(new { storageAttributeIds = ids.Reverse() })));
        foreach (string query in new[] { "?unknown=1", "?offset=0&offset=1", "?limit=1&limit=1", "?limit=", "?offset=%20", "?limit=1.0", "?offset=1e2", "?limit=101" })
            await Error(await Send(HttpMethod.Get, path + query), HttpStatusCode.BadRequest, ItemFamilyEndpoints.InvalidRequest);
        var page = await Body(await Send(HttpMethod.Get, path + "?offset=1&limit=1"));
        Assert.Equal(ids.Length, page.GetProperty("total").GetInt32());
        Assert.Equal(ids[1], Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Empty((await Body(await Send(HttpMethod.Get, path + "?offset=9223372036854775808"))).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task UpgradeFromStorageAttributesPreservesExistingRowsAndRunsOnlyNewMigrationOnce()
    {
        await using var original = await contexts.CreateDbContextAsync();
        var connection = new NpgsqlConnectionStringBuilder(original.Database.GetConnectionString());
        string schema = "family_upgrade_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(connection.ConnectionString);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin)) await create.ExecuteNonQueryAsync();
        connection.SearchPath = schema;
        var options = new DbContextOptionsBuilder<WmsDbContext>();
        WmsStorage.Configure(options, connection.ConnectionString, testing: true);
        try
        {
            await using var context = new WmsDbContext(options.Options);
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(context.GetService<IMigrationsIdGenerator>().GetName(StorageAttributeMigration));
            var before = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
            Assert.Equal(new[] { UomMigrationId, StorageAttributeMigration }, before);
            var uom = new UomRow { Id = Guid.NewGuid(), Code = Each, Description = "Retained UoM" };
            var attribute = new StorageAttributeRow { Id = Guid.NewGuid(), Code = Each, Name = "Retained attribute 📦" };
            context.Uoms.Add(uom); context.StorageAttributes.Add(attribute);
            await context.SaveChangesAsync();
            await context.Database.MigrateAsync();
            await context.Database.MigrateAsync();
            Assert.Equal(before.Append(ItemFamilyMigration).Append(Alxarafe.Modules.Wms.Infrastructure.Migrations.InventoryFoundation.MigrationId), await context.Database.GetAppliedMigrationsAsync());
            Assert.False(context.Database.HasPendingModelChanges());
            Assert.Empty(await context.ItemFamilies.ToArrayAsync());
            Assert.Empty(await context.ItemFamilyAssociations.ToArrayAsync());
            var retainedUom = await context.Uoms.AsNoTracking().SingleAsync();
            var retainedAttribute = await context.StorageAttributes.AsNoTracking().SingleAsync();
            Assert.Equal((uom.Id, uom.Code, uom.Description), (retainedUom.Id, retainedUom.Code, retainedUom.Description));
            Assert.Equal((attribute.Id, attribute.Code, attribute.Name), (retainedAttribute.Id, retainedAttribute.Code, retainedAttribute.Name));
            var pairType = context.Model.FindEntityType(typeof(ItemFamilyAssociationRow))!;
            Assert.Equal(2, pairType.GetForeignKeys().Count());
            Assert.Equal(2, pairType.FindPrimaryKey()!.Properties.Count);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
