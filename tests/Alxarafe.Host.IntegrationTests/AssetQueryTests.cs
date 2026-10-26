using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Alxarafe.Modules.Assets;
using Alxarafe.Resources;
using Alxarafe.Resources.AspNetCore;
using Alxarafe.Resources.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class AssetQueryTests(AssetsTestContext context) : IClassFixture<AssetsTestContext>
{
    private const string Exact = "12345678901234567890.123456789012345678";
    private static Guid Id(JsonElement row) => row.GetProperty(AssetFields.Id).GetGuid();
    private static JsonElement[] Items(JsonElement page) => page.GetProperty("items").EnumerateArray().ToArray();
    private static string Filter(string field, string op, object? value) => "filter=" + Uri.EscapeDataString(JsonSerializer.Serialize(new { field, @operator = op, value }));
    private static string TypeName(ScalarType type) => Enum.GetName(type)!.ToUpperInvariant();
    private async Task<JsonElement> Page(string resource, string query, string? token = null)
    {
        using var response = await context.Raw(HttpMethod.Get, AssetsTestContext.Path(resource) + "?" + query, null, token ?? context.Reader);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private async Task<Guid> Type(string? token = null) => Id(await context.Create(AssetResources.Type,
        new { code = Guid.NewGuid().ToString("N")[..AssetLimits.CodeLength], name = "Query fixture" }, token));
    private Task<JsonElement> Asset(Guid type, string name, string? token = null) => context.Create(AssetResources.Asset, new { name, assetTypeId = type }, token);
    private async Task Patch(Guid asset, object values)
    {
        using var response = await context.Send(HttpMethod.Patch, AssetResources.Asset, asset, values); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
    private async Task Error(string resource, string query, HttpStatusCode status = HttpStatusCode.BadRequest, string? token = null)
    {
        using var response = await context.Raw(HttpMethod.Get, AssetsTestContext.Path(resource) + "?" + query, null, token ?? context.Reader);
        Assert.Equal(status, response.StatusCode);
        if (status == HttpStatusCode.BadRequest) Assert.Equal(ResourceEndpoints.InvalidRequest, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("eq", 50, 1)]
    [InlineData("ne", 50, 2)]
    [InlineData("lt", 50, 1)]
    [InlineData("lte", 50, 2)]
    [InlineData("gt", 50, 1)]
    [InlineData("gte", 50, 2)]
    public async Task NumericPredicatesExecuteInPostgresBeforeAndFiltersTotalsAndPagination(string op, int bound, int expected)
    {
        var type = await Type();
        foreach (int condition in new[] { 0, 50, 100 })
        {
            var asset = await Asset(type, "Register item");
            await Patch(Id(asset), new { condition, locationId = AssetsSeed.Office, manufacturerId = AssetsSeed.Dell, assetModelId = AssetsSeed.Latitude });
        }
        await Asset(type, "No condition");
        string query = Filter(AssetFields.AssetTypeId, "eq", type) + "&" + Filter(AssetFields.Name, "eq", "Register item")
            + "&" + Filter(AssetFields.Condition, op, bound) + "&" + Filter(AssetFields.LocationId, "eq", AssetsSeed.Office)
            + "&" + Filter(AssetFields.ManufacturerId, "eq", AssetsSeed.Dell) + "&" + Filter(AssetFields.AssetModelId, "eq", AssetsSeed.Latitude)
            + "&" + Filter(AssetFields.Lifecycle, "eq", AssetLifecycle.Draft) + "&limit=1";
        var page = await Page(AssetResources.Asset, query); Assert.Equal(expected, page.GetProperty("total").GetInt32()); Assert.Single(Items(page));

    }

    [Fact]
    public async Task DatesDecimalsNullsAndTimestampOffsetsHaveExplicitPostgresSemantics()
    {
        var type = await Type(); var first = await Asset(type, "First"); var second = await Asset(type, "Second"); var absent = await Asset(type, "Absent");
        await Patch(Id(first), new { purchaseDate = "2024-02-29", purchasePrice = 10.01m });
        await Patch(Id(second), new { purchaseDate = "2024-03-01", purchasePrice = 10.02m });
        string owned = Filter(AssetFields.AssetTypeId, "eq", type);
        Assert.Equal(Id(first), Id(Assert.Single(Items(await Page(AssetResources.Asset, owned + "&" + Filter(AssetFields.PurchaseDate, "lt", "2024-03-01"))))));
        Assert.Equal(Id(second), Id(Assert.Single(Items(await Page(AssetResources.Asset, owned + "&" + Filter(AssetFields.PurchasePrice, "gt", 10.01m))))));
        Assert.Equal(Id(absent), Id(Assert.Single(Items(await Page(AssetResources.Asset, owned + "&" + Filter(AssetFields.PurchaseDate, "eq", null))))));
        Assert.Equal(2, Items(await Page(AssetResources.Asset, owned + "&" + Filter(AssetFields.PurchaseDate, "ne", null))).Length);
        // Nullable NE includes absent values under EF's existing compensated semantics.
        Assert.Equal(2, Items(await Page(AssetResources.Asset, owned + "&" + Filter(AssetFields.PurchasePrice, "ne", 10.01m))).Length);
        string instant = first.GetProperty(AssetFields.CreatedAt).GetDateTimeOffset().ToOffset(TimeSpan.FromHours(2)).ToString("O");
        Assert.Equal(Id(first), Id(Assert.Single(Items(await Page(AssetResources.Asset, Filter(AssetFields.Id, "eq", Id(first)) + "&" + Filter(AssetFields.CreatedAt, "eq", instant))))));
        Assert.Equal(3, Items(await Page(AssetResources.Asset, owned + "&" + Filter(AssetFields.UpdatedAt, "gte", "2000-01-01T00:00:00Z"))).Length);
        await Error(AssetResources.Asset, owned + "&" + Filter(AssetFields.PurchasePrice, "gt", null));
        var asc = Items(await Page(AssetResources.Asset, owned + "&sort=purchaseDate")); Assert.Equal(Id(absent), Id(asc[^1]));
        var desc = Items(await Page(AssetResources.Asset, owned + "&sort=-purchaseDate")); Assert.Equal(Id(absent), Id(desc[0]));
    }

    [Fact]
    public async Task TiedMultiColumnOrderingAndEveryOffsetWindowAreDeterministic()
    {
        var type = await Type(); var records = new List<(Guid Id, string Name, int Condition)>();
        foreach (var (name, condition) in new[] { ("B", 2), ("A", 2), ("B", 1), ("A", 1), ("A", 1), ("B", 1), ("A", 1) })
        {
            var asset = await Asset(type, name); await Patch(Id(asset), new { condition }); records.Add((Id(asset), name, condition));
        }
        string owned = Filter(AssetFields.AssetTypeId, "eq", type);
        var expected = records.OrderBy(row => row.Name, StringComparer.Ordinal).ThenByDescending(row => row.Condition).ThenBy(row => row.Id.ToString(), StringComparer.Ordinal).Select(row => row.Id).ToArray();
        Assert.Equal(expected, Items(await Page(AssetResources.Asset, owned + "&sort=name&sort=-condition")).Select(Id));
        var reversed = records.OrderByDescending(row => row.Name, StringComparer.Ordinal).ThenBy(row => row.Id.ToString(), StringComparer.Ordinal).Select(row => row.Id);
        Assert.Equal(reversed, Items(await Page(AssetResources.Asset, owned + "&sort=-name")).Select(Id));
        var windows = new List<Guid>();
        foreach (int offset in new[] { 0, 3, 6, 9 })
        {
            string query = owned + $"&sort=name&sort=-condition&offset={offset}&limit=3";
            var page = await Page(AssetResources.Asset, query); var repeated = await Page(AssetResources.Asset, query);
            Assert.Equal(7, page.GetProperty("total").GetInt32()); Assert.Equal(offset, page.GetProperty("offset").GetInt32()); Assert.Equal(3, page.GetProperty("limit").GetInt32());
            var actual = Items(page).Select(Id).ToArray(); Assert.Equal(expected.Skip(offset).Take(3), actual); Assert.Equal(actual, Items(repeated).Select(Id)); windows.AddRange(actual);
        }
        Assert.Equal(expected, windows); Assert.Equal(7, windows.Distinct().Count());
        var defaultOrder = records.OrderBy(row => row.Id.ToString(), StringComparer.Ordinal).Select(row => row.Id);
        Assert.Equal(defaultOrder, Items(await Page(AssetResources.Asset, owned)).Select(Id));
        var explicitId = records.OrderByDescending(row => row.Id.ToString(), StringComparer.Ordinal).Select(row => row.Id);
        Assert.Equal(explicitId, Items(await Page(AssetResources.Asset, owned + "&sort=-id&sort=name")).Select(Id));
        var filtered = await Page(AssetResources.Asset, owned + "&" + Filter(AssetFields.Condition, "eq", 1) + "&sort=name&sort=-condition&offset=3&limit=3");
        Assert.Equal(5, filtered.GetProperty("total").GetInt32());
        Assert.Equal(expected.Where(id => records.Single(row => row.Id == id).Condition == 1).Skip(3), Items(filtered).Select(Id));
        Assert.Empty(Items(await Page(AssetResources.Asset, owned + "&offset=2147483648")));
    }

    [Fact]
    public async Task SearchIsCaseSensitiveLiteralSubstringAcrossNameOrTagBeforePaging()
    {
        var type = await Type(); var a = await Asset(type, "Alpha 100%_Unit"); var b = await Asset(type, "Other"); await Asset(type, "alpha 100XYUnit");
        await Patch(Id(b), new { assetTag = "Tag 100%_Unit" });
        string owned = Filter(AssetFields.AssetTypeId, "eq", type);
        var page = await Page(AssetResources.Asset, owned + "&search=" + Uri.EscapeDataString("100%_Unit") + "&sort=name&offset=1&limit=1");
        Assert.Equal(2, page.GetProperty("total").GetInt32()); Assert.Equal(Id(b), Id(Assert.Single(Items(page))));
        Assert.Equal(Id(a), Id(Assert.Single(Items(await Page(AssetResources.Asset, owned + "&search=Alpha")))));
        Assert.Empty(Items(await Page(AssetResources.Asset, owned + "&search=ALPHA")));
        await Error(AssetResources.Asset, "search=" + Uri.EscapeDataString(char.MinValue.ToString()));
        foreach (string op in new[] { "contains", "startsWith", "eq" })
        {
            string operand = op == "contains" ? "%_" : op == "startsWith" ? "Tag 100%_" : "Tag 100%_Unit";
            Assert.Equal(Id(b), Id(Assert.Single(Items(await Page(AssetResources.Asset, owned + "&" + Filter(AssetFields.AssetTag, op, operand))))));
        }
        Assert.Empty(Items(await Page(AssetResources.Asset, owned + "&search=" + Uri.EscapeDataString("' OR true --"))));
    }

    [Theory]
    [InlineData("condition", "contains", "5")]
    [InlineData("condition", "eq", "50")]
    [InlineData("purchaseDate", "eq", "invalid")]
    [InlineData("lifecycle", "gt", "DRAFT")]
    [InlineData("locationId", "lt", "130e9201-e5a9-4739-8be0-000000000004")]
    [InlineData("location.name", "eq", "Office")]
    public async Task InvalidTypedOperatorsAndRelationPathsRemain400(string field, string op, string value) => await Error(AssetResources.Asset, Filter(field, op, value));

    private async Task<Guid> Define(Guid type, ScalarType scalar, string? token = null) => Id(await context.Create(AssetResources.FieldDefinition,
        new { assetTypeId = type, code = "q_" + Guid.NewGuid().ToString("N")[..12], name = "Query property", fieldType = TypeName(scalar) }, token));
    private Task<JsonElement> Value(Guid asset, Guid definition, string slot, object value, string? token = null) => context.Create(AssetResources.FieldValue,
        new Dictionary<string, object> { [AssetFields.AssetId] = asset, [AssetFields.FieldDefinitionId] = definition, [slot] = value }, token);

    [Theory]
    [InlineData(ScalarType.String, AssetFields.StringValue, "\"Alpha%_\"", "\"Beta\"", "lt")]
    [InlineData(ScalarType.Int32, AssetFields.Int32Value, "-2147483648", "2147483647", "lt")]
    [InlineData(ScalarType.Decimal, AssetFields.DecimalValue, "12345678901234567890.123456789012345677", Exact, "lt")]
    [InlineData(ScalarType.Boolean, AssetFields.BooleanValue, "false", "true", "ne")]
    [InlineData(ScalarType.Date, AssetFields.DateValue, "\"2024-02-29\"", "\"2024-03-01\"", "lt")]
    public async Task CanonicalDynamicSlotsExecuteTypedJsonbPredicatesAndPreserveTenantHistory(ScalarType scalar, string slot, string low, string high, string op)
    {
        var type = await Type(); var field = await Define(type, scalar); var a = await Asset(type, "Low"); var b = await Asset(type, "High");
        using var lowJson = JsonDocument.Parse(low); using var highJson = JsonDocument.Parse(high);
        var first = await Value(Id(a), field, slot, lowJson.RootElement); var second = await Value(Id(b), field, slot, highJson.RootElement);
        string owned = Filter(AssetFields.FieldDefinitionId, "eq", field);
        Assert.Equal(Id(first), Id(Assert.Single(Items(await Page(AssetResources.FieldValue, owned + "&" + Filter(slot, op, highJson.RootElement))))));
        Assert.Equal(Id(second), Id(Assert.Single(Items(await Page(AssetResources.FieldValue, owned + "&" + Filter(slot, "eq", highJson.RootElement))))));
        // Distinct bound parameters must survive composition of repeated slot filters.
        string range = owned + "&" + Filter(slot, scalar == ScalarType.Boolean ? "ne" : "gte", lowJson.RootElement) + "&" + Filter(slot, "eq", highJson.RootElement);
        Assert.Equal(Id(second), Id(Assert.Single(Items(await Page(AssetResources.FieldValue, range)))));
        Assert.Empty(Items(await Page(AssetResources.FieldValue, owned + "&" + Filter(slot, "eq", highJson.RootElement), context.OtherTenant)));
        var otherType = await Type(context.OtherTenant); var otherField = await Define(otherType, scalar, context.OtherTenant); var otherAsset = await Asset(otherType, "Other tenant", context.OtherTenant);
        var foreign = await Value(Id(otherAsset), otherField, slot, highJson.RootElement, context.OtherTenant);
        Assert.Contains(Items(await Page(AssetResources.FieldValue, Filter(slot, "eq", highJson.RootElement), context.OtherTenant)), row => Id(row) == Id(foreign));
        Assert.DoesNotContain(Items(await Page(AssetResources.FieldValue, Filter(slot, "eq", highJson.RootElement))), row => Id(row) == Id(foreign));
        using var disable = await context.Send(HttpMethod.Patch, AssetResources.FieldDefinition, field, new { active = false }); Assert.Equal(HttpStatusCode.OK, disable.StatusCode);
        Assert.Equal(Id(second), Id(Assert.Single(Items(await Page(AssetResources.FieldValue, owned + "&" + Filter(slot, "eq", highJson.RootElement))))));
    }

    [Fact]
    public async Task DynamicSlotTypeGuardsFragmentsAndRejectedOperatorsAreTruthful()
    {
        var type = await Type(); var asset = await Asset(type, "Mixed");
        var text = await Define(type, ScalarType.String); var number = await Define(type, ScalarType.Int32); var decimalField = await Define(type, ScalarType.Decimal);
        var row = await Value(Id(asset), text, AssetFields.StringValue, "' OR true -- %_日本語");
        await Value(Id(asset), number, AssetFields.Int32Value, 5); await Value(Id(asset), decimalField, AssetFields.DecimalValue, 5m);
        string owned = Filter(AssetFields.AssetId, "eq", Id(asset));
        Assert.Single(Items(await Page(AssetResources.FieldValue, owned + "&" + Filter(AssetFields.Int32Value, "eq", 5))));
        Assert.Single(Items(await Page(AssetResources.FieldValue, owned + "&" + Filter(AssetFields.DecimalValue, "eq", 5))));
        Assert.Single(Items(await Page(AssetResources.FieldValue, owned + "&" + Filter(AssetFields.Int32Value, "ne", 0))));
        foreach (string op in new[] { "contains", "startsWith" })
            Assert.Equal(Id(row), Id(Assert.Single(Items(await Page(AssetResources.FieldValue, owned + "&" + Filter(AssetFields.StringValue, op, "' OR true --"))))));
        Assert.Single(Items(await Page(AssetResources.FieldValue, owned + "&" + Filter(AssetFields.StringValue, "contains", "%_日本語"))));
        Assert.Empty(Items(await Page(AssetResources.FieldValue, owned + "&" + Filter(AssetFields.Int32Value, "eq", 5) + "&" + Filter(AssetFields.DecimalValue, "eq", 5))));
        await Error(AssetResources.FieldValue, Filter(AssetFields.BooleanValue, "gt", true));
        await Error(AssetResources.FieldValue, Filter(AssetFields.Int32Value, "eq", "5"));
        await Error(AssetResources.FieldValue, Filter(AssetFields.DateValue, "eq", "2024-02-30"));
        await Error(AssetResources.FieldValue, Filter(AssetFields.StringValue, "eq", null));
        await Error(AssetResources.FieldValue, "sort=decimalValue");
        await Error(AssetResources.FieldValue, Filter(AssetFields.StringValue, "contains", char.MinValue.ToString()));
        await Error(AssetResources.Asset, "", HttpStatusCode.Forbidden, context.NoPermission);
        await Error("assets.unknown-query", "", HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("eq", false, true)]
    [InlineData("ne", true, false)]
    [InlineData("lt", true, false)]
    [InlineData("lte", true, true)]
    [InlineData("gt", false, false)]
    [InlineData("gte", false, true)]
    public async Task EveryNumericAndDateDynamicComparisonExecutesWithExactBoundaries(string op, bool includesLow, bool includesHigh)
    {
        var type = await Type(); var a = await Asset(type, "Low"); var b = await Asset(type, "High");
        foreach (var (scalar, slot, low, high) in new[]
        {
            (ScalarType.Int32, AssetFields.Int32Value, "-1", "0"),
            (ScalarType.Decimal, AssetFields.DecimalValue, "12345678901234567890.123456789012345677", Exact),
            (ScalarType.Date, AssetFields.DateValue, "\"2024-02-29\"", "\"2024-03-01\"")
        })
        {
            var field = await Define(type, scalar); using var lowJson = JsonDocument.Parse(low); using var highJson = JsonDocument.Parse(high);
            var first = await Value(Id(a), field, slot, lowJson.RootElement); var second = await Value(Id(b), field, slot, highJson.RootElement);
            var page = await Page(AssetResources.FieldValue, Filter(AssetFields.FieldDefinitionId, "eq", field) + "&" + Filter(slot, op, highJson.RootElement));
            var expected = new List<Guid>(); if (includesLow) expected.Add(Id(first)); if (includesHigh) expected.Add(Id(second));
            Assert.Equal(expected.Order(), Items(page).Select(Id).Order()); Assert.Equal(expected.Count, page.GetProperty("total").GetInt32());
        }
    }

    [Fact]
    public async Task TypedQueriesExecuteDirectlyThroughRuntimeWithAuthoritativeTenantContext()
    {
        var type = await Type(); var asset = await Asset(type, "Direct Runtime"); var field = await Define(type, ScalarType.Decimal);
        using var exact = JsonDocument.Parse(Exact); var value = await Value(Id(asset), field, AssetFields.DecimalValue, exact.RootElement);
        await using var scope = context.Host.Services.CreateAsyncScope(); var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider, User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, context.FullUser.ToString())], "fixture")) };
        try
        {
            var runtime = scope.ServiceProvider.GetRequiredService<ResourceRuntime>();
            var query = new ResourceQuery(filters: [new(AssetFields.FieldDefinitionId, ResourceFilterOperator.Equal, new UuidValue(field)),
                new(AssetFields.DecimalValue, ResourceFilterOperator.GreaterThanOrEqual, new NumericValue(ResourceNumber.Parse(Exact)))], sorting: [new(AssetFields.Id)]);
            var page = await runtime.ListAsync(AssetResources.FieldValue, query);
            Assert.Equal(new UuidValue(Id(value)), Assert.Single(page.Items).Values[AssetFields.Id]); Assert.Equal(1, page.Total);
            Assert.Equal(new NumericValue(ResourceNumber.Parse(Exact)), page.Items[0].Values[AssetFields.DecimalValue]);
        }
        finally { accessor.HttpContext = null; }
    }

    [Fact]
    public async Task MetadataAdvertisesOnlyExecutableCapabilitiesAndPhysicalSchemaStaysFixed()
    {
        async Task<JsonElement> Metadata(string resource)
        {
            using var response = await context.Raw(HttpMethod.Get, ResourceEndpoints.MetadataPath + "/" + resource, null, context.Reader);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        var asset = await Metadata(AssetResources.Asset);
        var searchable = asset.GetProperty("fields").EnumerateArray().Where(f => f.GetProperty("capabilities").GetProperty("searchable").GetBoolean()).Select(f => f.GetProperty("name").GetString());
        Assert.Equal(new[] { AssetFields.AssetTag, AssetFields.Name }, searchable);
        foreach (string name in new[] { AssetFields.Name, AssetFields.AssetTag, AssetFields.Condition, AssetFields.PurchasePrice, AssetFields.PurchaseDate, AssetFields.CreatedAt, AssetFields.UpdatedAt })
        {
            var capabilities = asset.GetProperty("fields").EnumerateArray().Single(f => f.GetProperty("name").GetString() == name).GetProperty("capabilities");
            Assert.True(capabilities.GetProperty("filterable").GetBoolean()); Assert.True(capabilities.GetProperty("sortable").GetBoolean());
        }
        var values = await Metadata(AssetResources.FieldValue);
        foreach (string slot in new[] { AssetFields.StringValue, AssetFields.Int32Value, AssetFields.DecimalValue, AssetFields.BooleanValue, AssetFields.DateValue })
        {
            var field = values.GetProperty("fields").EnumerateArray().Single(f => f.GetProperty("name").GetString() == slot);
            var capability = field.GetProperty("capabilities"); Assert.True(capability.GetProperty("filterable").GetBoolean()); Assert.False(capability.GetProperty("sortable").GetBoolean()); Assert.False(capability.GetProperty("searchable").GetBoolean());
        }
        await using var db = await context.Database(); Assert.False(db.Database.HasPendingModelChanges());
        var columns = db.Model.FindEntityType(typeof(AssetFieldValueRow))!.GetProperties(); Assert.DoesNotContain(columns, column => column.Name.EndsWith("Value", StringComparison.Ordinal));
    }
}
