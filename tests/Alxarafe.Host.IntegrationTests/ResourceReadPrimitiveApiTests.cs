using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Alxarafe.Core;
using Alxarafe.Modularity;
using Alxarafe.Modules.Ai.Application;
using Alxarafe.Modules.Assets;
using Alxarafe.Modules.Wms;
using Alxarafe.Resources.AspNetCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Alxarafe.Host.IntegrationTests;

public sealed class ResourceReadPrimitiveApiTests(ResourceReadPrimitiveContext context, ITestOutputHelper output) : IClassFixture<ResourceReadPrimitiveContext>
{
    private static readonly string[] LowRamTags = ["ERB-LT-002", "ERB-LT-005", "ERB-LT-006"];
    private static JsonElement Json(object? input = null) => JsonSerializer.SerializeToElement(input ?? new { });
    private static object Filter(string field, string op, object? value) => new { field, @operator = op, value };
    private static object Query(params object[] filters) => new { filters };
    private static JsonElement[] Items(JsonElement page) => page.GetProperty("items").EnumerateArray().ToArray();
    private sealed record Step(string Resource, Func<IReadOnlyList<JsonElement>, object> Arguments);
    private static Step Read(string resource, object? args = null) => new(resource, _ => args ?? new { });
    private async Task<(JsonElement Body, IReadOnlyList<JsonElement> Pages)> Plan(string question, Func<IReadOnlyList<JsonElement>, string> answer, params Step[] steps)
    {
        var pages = new List<JsonElement>(); int turn = 0; string parentId = "";
        context.Complete = request =>
        {
            Assert.False(string.IsNullOrWhiteSpace(request.InvocationId));
            if (turn == 0) parentId = request.InvocationId!; else Assert.Equal(parentId, request.InvocationId);
            Assert.DoesNotContain("RAM_GB", request.SystemPrompt, StringComparison.Ordinal);
            Assert.DoesNotContain("Assets", request.SystemPrompt, StringComparison.Ordinal);
            if (turn > 0)
            {
                var result = request.Messages.Last(message => message.Role == AiRoles.Tool);
                pages.Add(JsonDocument.Parse(result.Content!).RootElement.Clone());
            }
            if (turn == steps.Length) return Task.FromResult(new AiModelResponse(answer(pages), []));
            var step = steps[turn++]; var tool = Assert.Single(request.Tools, tool => tool.PrimitiveId == ResourceReadPrimitives.Id(step.Resource));
            Assert.False(tool.InputSchema.GetProperty("additionalProperties").GetBoolean());
            return Task.FromResult(new AiModelResponse(null, [new("resource-call-" + turn, tool.Name, Json(step.Arguments(pages)))]));
        };
        using var response = await context.Ask(question);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var used = body.GetProperty("toolsUsed").EnumerateArray().ToArray();
        Assert.Equal(steps.Select(step => ResourceReadPrimitives.Id(step.Resource)), used.Select(tool => tool.GetProperty("primitiveId").GetString()));
        Assert.All(used, tool => { Assert.Equal(2, tool.EnumerateObject().Count()); Assert.False(string.IsNullOrWhiteSpace(tool.GetProperty("invocationId").GetString())); });
        for (int index = 0; index < used.Length; index++)
        {
            string id = used[index].GetProperty("invocationId").GetString()!;
            var audit = Assert.Single(context.Capture.Lines, line => line.Contains("invocation " + id, StringComparison.Ordinal));
            Assert.Contains("aiInvocationId=" + parentId, audit, StringComparison.Ordinal);
            Assert.Contains("sequence=" + (index + 1), audit, StringComparison.Ordinal);
        }
        Assert.Equal(answer(pages), body.GetProperty("answer").GetString()); Assert.InRange(used.Length, 1, 8);
        output.WriteLine("{0}: {1}; tool calls={2}", question, body.GetProperty("answer").GetString(), used.Length);
        return (body, pages);
    }
    private Task<(JsonElement Body, IReadOnlyList<JsonElement> Pages)> Count(string question, string resource, int expected, object? args = null) =>
        Plan(question, pages => { Assert.Equal(expected, pages[0].GetProperty("total").GetInt32()); return "Total: " + pages[0].GetProperty("total").GetInt32(); }, Read(resource, args));

    [Fact]
    public async Task CanonicalTotalsDraftsNullRepairAndLowConditionComeFromRealResourcePrimitives()
    {
        await Count("¿Cuántos activos hay?", AssetResources.Asset, 24);
        await Count("¿Qué activos están en borrador?", AssetResources.Asset, 5, Query(Filter(AssetFields.Lifecycle, "eq", AssetLifecycle.Draft)));
        await Count("¿Qué activos no tienen ubicación?", AssetResources.Asset, 6, Query(Filter(AssetFields.LocationId, "eq", null)));
        await Count("¿Qué activos tienen condición inferior a 50?", AssetResources.Asset, 6, Query(Filter(AssetFields.Condition, "lt", 50)));
        var result = await Plan("¿Qué activos están en reparación?", pages => "Total: " + pages[1].GetProperty("total").GetInt32(),
            Read(AssetResources.Status, Query(Filter(AssetFields.Code, "eq", AssetStatusCodes.InRepair))),
            new(AssetResources.Asset, pages => Query(Filter(AssetFields.StatusId, "eq", Items(pages[0]).Single().GetProperty(AssetFields.Id).GetString()))));
        Assert.Equal(3, result.Pages[1].GetProperty("total").GetInt32());
    }
    [Fact]
    public async Task LaptopQuestionComposesTypeResolutionAndAssetFilterWithOrderedProvenance()
    {
        var result = await Plan("¿Cuántos portátiles tenemos?", pages => "Hay " + pages[1].GetProperty("total").GetInt32() + " portátiles.",
            Read(AssetResources.Type, Query(Filter(AssetFields.Code, "eq", "LAPTOP"))),
            new(AssetResources.Asset, pages => Query(Filter(AssetFields.AssetTypeId, "eq", Items(pages[0]).Single().GetProperty(AssetFields.Id).GetString()))));
        Assert.Equal("Hay 8 portátiles.", result.Body.GetProperty("answer").GetString());
        Assert.Equal(8, Items(result.Pages[1]).Length);
    }
    [Fact]
    public async Task TypeAndManufacturerDistributionsNeedOnlyListsAndClientSideComposition()
    {
        var result = await Plan("¿Cuántos activos tenemos por tipo y fabricante?", pages => "Leídas " + Items(pages[0]).Length + " filas.",
            Read(AssetResources.Asset), Read(AssetResources.Type), Read(AssetResources.Manufacturer), Read(AssetResources.Model));
        var assets = Items(result.Pages[0]); var types = Items(result.Pages[1]); var makers = Items(result.Pages[2]); var models = Items(result.Pages[3]);
        var typeCounts = assets.GroupBy(asset => types.Single(type => type.GetProperty("id").GetGuid() == asset.GetProperty(AssetFields.AssetTypeId).GetGuid()).GetProperty("code").GetString()!)
            .ToDictionary(group => group.Key, group => group.Count());
        Assert.Equal(8, typeCounts["LAPTOP"]); Assert.Equal(5, typeCounts["MONITOR"]); Assert.Equal(4, typeCounts["PHONE"]); Assert.Equal(3, typeCounts["PRINTER"]); Assert.Equal(4, typeCounts["SERVER"]);
        var makerCounts = assets.GroupBy(asset => asset.GetProperty(AssetFields.ManufacturerId).ValueKind == JsonValueKind.Null ? "unspecified" : makers.Single(maker => maker.GetProperty("id").GetGuid() == asset.GetProperty(AssetFields.ManufacturerId).GetGuid()).GetProperty("name").GetString()!)
            .ToDictionary(group => group.Key, group => group.Count());
        Assert.Equal(6, makerCounts["Dell"]); Assert.Equal(3, makerCounts["Lenovo"]); Assert.Equal(7, makerCounts["HP"]); Assert.Equal(3, makerCounts["Samsung"]); Assert.Equal(3, makerCounts["Apple"]); Assert.Equal(2, makerCounts["unspecified"]);
        Assert.Equal(13, models.Length);
        Assert.Equal(16, assets.Count(asset => asset.GetProperty(AssetFields.Lifecycle).GetString() == AssetLifecycle.Ready));
        Assert.Equal(3, assets.Count(asset => asset.GetProperty(AssetFields.Lifecycle).GetString() == AssetLifecycle.Retired));
        foreach (var asset in assets.Where(asset => asset.GetProperty(AssetFields.AssetModelId).ValueKind != JsonValueKind.Null))
            Assert.Equal(asset.GetProperty(AssetFields.ManufacturerId).GetGuid(), models.Single(model => model.GetProperty("id").GetGuid() == asset.GetProperty(AssetFields.AssetModelId).GetGuid()).GetProperty(AssetFields.ManufacturerId).GetGuid());
        output.WriteLine("Types=8/5/4/3/4; makers=Dell6/Lenovo3/HP7/Samsung3/Apple3/unspecified2; tool calls=4");
    }
    [Fact]
    public async Task AssignmentHistoryAndLocationCanBeResolvedWithoutJoinsOrBusinessTools()
    {
        await Count("¿Qué activos tienen asignación actual?", AssetResources.Assignment, 8, Query(Filter(AssetFields.EndedAt, "eq", null)));
        await Count("¿Qué historial cerrado hay?", AssetResources.Assignment, 4, Query(Filter(AssetFields.EndedAt, "ne", null)));
        var result = await Plan("¿Dónde está asignado ERB-LT-001?", pages => Items(pages[2]).Single().GetProperty("name").GetString()!,
            Read(AssetResources.Asset, Query(Filter(AssetFields.AssetTag, "eq", "ERB-LT-001"))),
            new(AssetResources.Assignment, pages => Query(Filter(AssetFields.AssetId, "eq", Items(pages[0]).Single().GetProperty("id").GetString()), Filter(AssetFields.EndedAt, "eq", null))),
            new(AssetResources.Location, pages => Query(Filter(AssetFields.Id, "eq", Items(pages[1]).Single().GetProperty(AssetFields.TargetId).GetString()))));
        Assert.Equal("Main office", result.Body.GetProperty("answer").GetString());
    }
    [Fact]
    public async Task LowRamUsesDefinitionsTypedValuesAndAssetsWithoutDuplicatingDynamicRules()
    {
        var result = await Plan("¿Qué portátiles tienen menos de 16 GB de RAM?", pages => "Encontrados " + Items(pages[1]).Length + ".",
            Read(AssetResources.FieldDefinition, Query(Filter(AssetFields.Code, "eq", "RAM_GB"))),
            new(AssetResources.FieldValue, pages => Query(Filter(AssetFields.FieldDefinitionId, "eq", Items(pages[0]).Single().GetProperty("id").GetString()), Filter(Items(pages[0]).Single().GetProperty(AssetFields.ValueField).GetString()!, "lt", 16))),
            new(AssetResources.Asset, pages => Query(Filter(AssetFields.AssetTypeId, "eq", Items(pages[0]).Single().GetProperty(AssetFields.AssetTypeId).GetString()))));
        Assert.Equal(3, Items(result.Pages[1]).Length);
        var all = await Count("Valores dinámicos persistidos", AssetResources.FieldValue, 17);
        Assert.Contains(Items(all.Pages[0]), value => value.TryGetProperty(AssetFields.BooleanValue, out var boolean) && boolean.ValueKind == JsonValueKind.False);
        var ids = Items(result.Pages[1]).Select(value => value.GetProperty(AssetFields.AssetId).GetGuid()).ToHashSet();
        var tags = Items(result.Pages[2]).Where(asset => ids.Contains(asset.GetProperty("id").GetGuid())).Select(asset => asset.GetProperty(AssetFields.AssetTag).GetString()).Order().ToArray();
        Assert.Equal(LowRamTags, tags);
    }
    [Theory]
    [InlineData("New laptop awaiting details", 2)]
    [InlineData("Laptop awaiting encryption details", 1)]
    public async Task DraftMissingRequiredInformationIsObservableWithoutCopyingRequireComplete(string name, int missing)
    {
        var result = await Plan("¿Qué información le falta a " + name + "?", _ => "Información disponible para revisión.",
            Read(AssetResources.Asset, Query(Filter(AssetFields.Name, "eq", name))),
            new(AssetResources.FieldDefinition, pages => Query(Filter(AssetFields.AssetTypeId, "eq", Items(pages[0]).Single().GetProperty(AssetFields.AssetTypeId).GetString()))),
            new(AssetResources.FieldValue, pages => Query(Filter(AssetFields.AssetId, "eq", Items(pages[0]).Single().GetProperty("id").GetString()))));
        Assert.Equal(3, Items(result.Pages[1]).Length);
        var values = Items(result.Pages[2]).Select(value => value.GetProperty(AssetFields.FieldDefinitionId).GetGuid()).ToHashSet();
        var absent = Items(result.Pages[1]).Where(field => field.GetProperty(AssetFields.Required).GetBoolean() && !values.Contains(field.GetProperty("id").GetGuid())).ToArray();
        Assert.Equal(missing, absent.Length); Assert.Contains(absent, row => row.GetProperty("code").GetString() == "ENCRYPTED");
        output.WriteLine("Missing required fields={0}; tool calls=3", missing);
    }

    [Theory]
    [InlineData("lt", 6)]
    [InlineData("lte", 6)]
    [InlineData("gt", 13)]
    [InlineData("gte", 13)]
    public async Task OrderedOperatorsExecuteAgainstPostgreSql(string op, int count) => await Count("Condition comparison", AssetResources.Asset, count, Query(Filter(AssetFields.Condition, op, 50)));
    [Theory]
    [InlineData("eq", "LAPTOP", 1)]
    [InlineData("ne", "LAPTOP", 4)]
    [InlineData("contains", "TOP", 1)]
    [InlineData("startsWith", "LAP", 1)]
    public async Task TextOperatorsExecuteAgainstPostgreSql(string op, string text, int count) => await Count("Catalogue comparison", AssetResources.Type, count, Query(Filter(AssetFields.Code, op, text)));
    [Fact]
    public async Task TypedProjectionPreservesPaginationSearchAndBusinessJsonWithoutClrWrappers()
    {
        var page = await context.Probe(ResourceReadPrimitives.Id(AssetResources.Asset), Json(new { offset = 2, limit = 2 }));
        Assert.Equal(ReadPrimitiveOutcome.Succeeded, page.Outcome); var data = page.Output!.Value;
        Assert.Equal(24, data.GetProperty("total").GetInt32()); Assert.Equal(2, data.GetProperty("offset").GetInt32()); Assert.Equal(2, data.GetProperty("limit").GetInt32()); Assert.Equal(2, Items(data).Length);
        Assert.Equal(40, Items(data)[0].GetProperty(AssetFields.Condition).GetInt32()); Assert.Equal(JsonValueKind.String, Items(data)[0].GetProperty(AssetFields.Id).ValueKind);
        Assert.Equal(JsonValueKind.Number, Items(data)[0].GetProperty(AssetFields.PurchasePrice).ValueKind);
        Assert.DoesNotContain("ScalarValue", data.GetRawText(), StringComparison.Ordinal);
        await Count("Search operations", AssetResources.Asset, 3, new { search = "Operations" });
        var noSearch = await context.Probe(ResourceReadPrimitives.Id(AssetResources.Type), Json(new { search = "Laptop" }));
        Assert.Equal(ReadPrimitiveOutcome.InvalidRequest, noSearch.Outcome);
    }
    [Theory]
    [InlineData("[]")]
    [InlineData("{\"tenantId\":\"other\"}")]
    [InlineData("{\"operation\":\"delete\"}")]
    [InlineData("{\"url\":\"http://untrusted\"}")]
    [InlineData("{\"sort\":[\"name\"]}")]
    [InlineData("{\"limit\":0}")]
    [InlineData("{\"limit\":101}")]
    [InlineData("{\"offset\":-1}")]
    [InlineData("{\"limit\":\"2\"}")]
    [InlineData("{\"limit\":2,\"limit\":3}")]
    [InlineData("{\"filters\":[{\"field\":\"condition\",\"operator\":\"lt\",\"value\":\"50\"}]}")]
    [InlineData("{\"filters\":[{\"field\":\"condition\",\"operator\":\"lt\",\"value\":null}]}")]
    [InlineData("{\"filters\":[{\"field\":\"id\",\"operator\":\"contains\",\"value\":\"abc\"}]}")]
    [InlineData("{\"filters\":[{\"field\":\"madeUp\",\"operator\":\"eq\",\"value\":1}]}")]
    [InlineData("{\"filters\":[{\"field\":\"condition\",\"operator\":\"eq\",\"value\":101}]}")]
    [InlineData("{\"filters\":[{\"field\":\"name\",\"operator\":\"eq\",\"value\":null}]}")]
    [InlineData("{\"filters\":[{\"field\":\"locationId\",\"operator\":\"eq\",\"value\":\"null\"}]}")]
    [InlineData("{\"filters\":[{\"field\":\"name\",\"operator\":\"sql\",\"value\":\"x\"}]}")]
    [InlineData("{\"filters\":[{\"field\":\"name\",\"operator\":\"eq\",\"value\":\"x\",\"extra\":1}]}")]
    public async Task ForgedArgumentsAreInvalidRequestsRatherThanQueriesOrWrites(string json)
    {
        var result = await context.Probe(ResourceReadPrimitives.Id(AssetResources.Asset), JsonDocument.Parse(json).RootElement.Clone());
        Assert.Equal(ReadPrimitiveOutcome.InvalidRequest, result.Outcome); Assert.Null(result.Output);
    }
    [Fact]
    public async Task InvokePermissionDoesNotGrantResourceReadAndKnownForbiddenPrimitiveIsDenied()
    {
        var result = await context.Probe(ResourceReadPrimitives.Id(AssetResources.Asset), Json(), context.InvokeOnlyToken,
            request => Assert.DoesNotContain(request.Tools, tool => tool.PrimitiveId?.StartsWith("assets.", StringComparison.Ordinal) == true));
        Assert.Equal(ReadPrimitiveOutcome.Denied, result.Outcome); Assert.Null(result.Output);
        Assert.NotEqual(ReadPrimitiveOutcome.NotFound, result.Outcome);
        var allowed = await context.Probe(ResourceReadPrimitives.Id(AssetResources.Asset), Json(), inspect:
            request => Assert.Equal(9, request.Tools.Count(tool => tool.PrimitiveId?.StartsWith("assets.", StringComparison.Ordinal) == true)));
        Assert.Equal(ReadPrimitiveOutcome.Succeeded, allowed.Outcome);
        await context.Probe(ResourceReadPrimitives.Id(AssetResources.Asset), Json(), context.AdminToken, request =>
        {
            Assert.Contains(request.Tools, tool => tool.PrimitiveId == WmsReadPrimitives.Inventory);
            Assert.Contains(request.Tools, tool => tool.PrimitiveId == ResourceReadPrimitives.Id(AssetResources.Asset));
        });
    }
    [Fact]
    public async Task RevocationAfterDiscoveryDeniesTheNextToolCallWithoutNewLoginOrConversation()
    {
        context.Complete = async request =>
        {
            var tool = Assert.Single(request.Tools, tool => tool.PrimitiveId == ResourceReadPrimitives.Id(AssetResources.Asset));
            await context.Permission(false);
            return new(null, [new("revoked", tool.Name, Json())]);
        };
        try
        {
            using var response = await context.Ask("Read after permission change"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Empty((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("toolsUsed").EnumerateArray());
            Assert.Contains(context.Capture.Lines, line => line.Contains("primitive=" + ResourceReadPrimitives.Id(AssetResources.Asset), StringComparison.Ordinal) && line.Contains("outcome=Denied", StringComparison.Ordinal));
            var result = await context.Probe(ResourceReadPrimitives.Id(AssetResources.Asset), Json(), inspect: request => Assert.DoesNotContain(request.Tools, tool => tool.PrimitiveId?.StartsWith("assets.", StringComparison.Ordinal) == true));
            Assert.Equal(ReadPrimitiveOutcome.Denied, result.Outcome);
        }
        finally { await context.Permission(true); }
    }
    [Fact]
    public async Task DisabledModuleDisappearsAndPreviouslyKnownPrimitiveIsUnavailable()
    {
        var states = context.Services.GetRequiredService<ModuleStateService>();
        await states.DisableAsync(AssetsModule.Id);
        try
        {
            var result = await context.Probe(ResourceReadPrimitives.Id(AssetResources.Asset), Json(), inspect: request => Assert.DoesNotContain(request.Tools, tool => tool.PrimitiveId?.StartsWith("assets.", StringComparison.Ordinal) == true));
            Assert.Equal(ReadPrimitiveOutcome.Unavailable, result.Outcome);
        }
        finally { await states.EnableAsync(AssetsModule.Id); }
    }
    [Fact]
    public async Task NewlyCreatedDraftIsImmediatelyObservableAndTenantSwitchNeverLeaksIt()
    {
        using var created = await context.Send(HttpMethod.Post, ResourceEndpoints.ResourcePath + "/" + AssetResources.Asset,
            new { name = "Portátil demo IA", assetTypeId = AssetsSeed.Laptop }, context.AdminToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode); var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        try
        {
            await Count("Busca Portátil demo IA", AssetResources.Asset, 1, Query(Filter(AssetFields.Name, "eq", "Portátil demo IA")));
            await context.Membership(ResourceReadPrimitiveContext.SecondaryTenant);
            await Count("Busca Portátil demo IA en tenant actual", AssetResources.Asset, 0, Query(Filter(AssetFields.Name, "eq", "Portátil demo IA")));
            await context.Membership(DevelopmentTenantBootstrap.TenantId);
            await Count("Busca de nuevo Portátil demo IA", AssetResources.Asset, 1, Query(Filter(AssetFields.Name, "eq", "Portátil demo IA")));
        }
        finally
        {
            await context.Membership(DevelopmentTenantBootstrap.TenantId);
            using var deleted = await context.Send(HttpMethod.Delete, ResourceEndpoints.ResourcePath + "/" + AssetResources.Asset + "/" + id, token: context.AdminToken);
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }
    }
    [Fact]
    public async Task AuditRecordsProvenancePagingBytesAndNoBusinessPayloadOrAddedWmsPermissions()
    {
        var result = await Count("Audit proof", AssetResources.Asset, 24, new { offset = 0, limit = 2 });
        string invocation = Assert.Single(result.Body.GetProperty("toolsUsed").EnumerateArray()).GetProperty("invocationId").GetString()!;
        var line = Assert.Single(context.Capture.Lines, line => line.Contains("invocation " + invocation, StringComparison.Ordinal));
        foreach (string required in new[] { "aiInvocationId=", "module=" + AssetsModule.Id, "subject=" + context.ReaderId, "sequence=1", "durationMs=", "outcome=Succeeded", "offset=0", "limit=2", "returned=2", "total=24", "outputBytes=" }) Assert.Contains(required, line, StringComparison.Ordinal);
        Assert.DoesNotContain("Operations laptop", line, StringComparison.Ordinal); Assert.DoesNotContain("DEMO-SN", line, StringComparison.Ordinal);
        await context.Probe(ResourceReadPrimitives.Id(AssetResources.Asset), Json(), inspect: request => Assert.DoesNotContain(request.Tools, tool => WmsReadPrimitives.All.Any(wms => wms.Id == tool.PrimitiveId)));
    }
}
