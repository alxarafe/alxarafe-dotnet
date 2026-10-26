using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
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

public sealed class AssetDynamicFieldTests(AssetsTestContext context) : IClassFixture<AssetsTestContext>
{
    private const string StringType = "STRING";
    private const string IntegerType = "INT32";
    private const string DecimalType = "DECIMAL";
    private const string BooleanType = "BOOLEAN";
    private const string DateType = "DATE";
    private const string ExactDecimal = "12345678901234567890.123456789012345678";
    private static Guid Id(JsonElement row) => row.GetProperty(AssetFields.Id).GetGuid();
    private static async Task<JsonElement> Body(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();
    private async Task<JsonElement> Type(string? token = null) => await context.Create(AssetResources.Type,
        new { code = Guid.NewGuid().ToString("N")[..AssetLimits.CodeLength], name = "Dynamic type" }, token);
    private Task<JsonElement> Asset(Guid type, string? token = null) => context.Create(AssetResources.Asset, new { name = "Dynamic asset", assetTypeId = type }, token);
    private Task<JsonElement> Define(Guid type, string fieldType = StringType, bool required = false, bool active = true, string? code = null, string? token = null) =>
        context.Create(AssetResources.FieldDefinition, Definition(type, fieldType, required, active, code), token);
    private static object Definition(Guid type, string fieldType, bool required, bool active, string? code = null) =>
        new { assetTypeId = type, code = code ?? "field_" + Guid.NewGuid().ToString("N")[..12], name = "Dynamic field", fieldType, required, active };
    private static Dictionary<string, object?> Value(Guid asset, Guid definition, string slot, object? value) =>
        new() { [AssetFields.AssetId] = asset, [AssetFields.FieldDefinitionId] = definition, [slot] = value };
    private async Task<JsonElement> Read(string resource, Guid id, string? token = null)
    {
        using var response = await context.Send(HttpMethod.Get, resource, id, token: token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); return await Body(response);
    }
    private static async Task Error(HttpResponseMessage response, HttpStatusCode expected, string? reason = null)
    {
        using (response)
        {
            Assert.Equal(expected, response.StatusCode); var body = await Body(response);
            if (expected == HttpStatusCode.BadRequest) Assert.Equal(ResourceEndpoints.InvalidRequest, body.GetProperty("code").GetString());
            if (expected == HttpStatusCode.Conflict) Assert.Contains(body.GetProperty("code").GetString(), new[] { ResourceEndpoints.Conflict, "resource_action_conflict" });
            if (reason is not null) Assert.Equal(reason, body.GetProperty("errors")[0].GetProperty("code").GetString());
        }
    }
    private static string Filter(string field, object value) => Uri.EscapeDataString(JsonSerializer.Serialize(new { field, @operator = "eq", value }));

    [Fact]
    public async Task RequiredFieldsKeepDraftCreationMinimalAndCompletionUsesPresenceAndValidity()
    {
        var type = await Type(); var field = await Define(Id(type), IntegerType, required: true);
        var asset = await Asset(Id(type));
        Assert.Equal(AssetLifecycle.Draft, asset.GetProperty(AssetFields.Lifecycle).GetString());
        await using (var db = await context.Database()) Assert.False(await db.FieldValues.AnyAsync(value => value.AssetId == Id(asset)));
        await Error(await context.Action(Id(asset), AssetActions.Complete), HttpStatusCode.Conflict, AssetFailures.RequiredFields);
        Assert.Equal(asset.ToString(), (await Read(AssetResources.Asset, Id(asset))).ToString());
        var value = await context.Create(AssetResources.FieldValue, Value(Id(asset), Id(field), AssetFields.Int32Value, 0));
        using var complete = await context.Action(Id(asset), AssetActions.Complete, context.Writer);
        Assert.Equal(HttpStatusCode.NoContent, complete.StatusCode);
        Assert.Equal(AssetLifecycle.Ready, (await Read(AssetResources.Asset, Id(asset))).GetProperty(AssetFields.Lifecycle).GetString());
        await Error(await context.Send(HttpMethod.Delete, AssetResources.FieldValue, Id(value)), HttpStatusCode.Conflict, AssetFailures.RequiredValue);
        using var patch = await context.Send(HttpMethod.Patch, AssetResources.FieldValue, Id(value), new { int32Value = 1 });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        Assert.Equal(1, (await Read(AssetResources.FieldValue, Id(value))).GetProperty(AssetFields.Int32Value).GetInt32());
    }

    [Theory]
    [InlineData(StringType, AssetFields.StringValue, "\"Hello Ω\"")]
    [InlineData(IntegerType, AssetFields.Int32Value, "-2147483648")]
    [InlineData(DecimalType, AssetFields.DecimalValue, ExactDecimal)]
    [InlineData(BooleanType, AssetFields.BooleanValue, "false")]
    [InlineData(DateType, AssetFields.DateValue, "\"2024-02-29\"")]
    public async Task CanonicalScalarTypesRoundTripThroughPostgresAndRuntime(string fieldType, string slot, string json)
    {
        var type = await Type(); var field = await Define(Id(type), fieldType, required: true); var asset = await Asset(Id(type));
        using var document = JsonDocument.Parse(json);
        var created = await context.Create(AssetResources.FieldValue, Value(Id(asset), Id(field), slot, document.RootElement));
        var read = await Read(AssetResources.FieldValue, Id(created));
        var expected = ResourceTransport.Project(ResourceTransport.Parse(Enum.Parse<ScalarType>(fieldType, ignoreCase: true), document.RootElement));
        Assert.Equal(expected.GetRawText(), read.GetProperty(slot).GetRawText());
        string[] slots = [AssetFields.StringValue, AssetFields.Int32Value, AssetFields.DecimalValue, AssetFields.BooleanValue, AssetFields.DateValue];
        Assert.Single(slots, candidate => read.TryGetProperty(candidate, out _));
        await using var db = await context.Database();
        var row = await db.FieldValues.SingleAsync(value => value.Id == Id(created));
        using var stored = JsonDocument.Parse(row.ValueJson);
        Assert.Equal(document.RootElement.ValueKind, stored.RootElement.ValueKind);
        using var complete = await context.Action(Id(asset), AssetActions.Complete); Assert.Equal(HttpStatusCode.NoContent, complete.StatusCode);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"stringValue\":\"12\"}")]
    [InlineData("{\"int32Value\":1,\"booleanValue\":false}")]
    [InlineData("{\"int32Value\":null}")]
    [InlineData("{\"int32Value\":\"1\"}")]
    [InlineData("{\"int32Value\":1.5}")]
    [InlineData("{\"int32Value\":2147483648}")]
    [InlineData("{\"value\":1}")]
    public async Task ExactlyOneMatchingNonNullTypedSlotIsRequired(string patch)
    {
        var type = await Type(); var field = await Define(Id(type), IntegerType); var asset = await Asset(Id(type));
        using var partial = JsonDocument.Parse(patch);
        var create = new Dictionary<string, object?> { [AssetFields.AssetId] = Id(asset), [AssetFields.FieldDefinitionId] = Id(field) };
        foreach (var property in partial.RootElement.EnumerateObject()) create.Add(property.Name, property.Value.Clone());
        await Error(await context.Send(HttpMethod.Post, AssetResources.FieldValue, body: create), HttpStatusCode.BadRequest);
        var value = await context.Create(AssetResources.FieldValue, Value(Id(asset), Id(field), AssetFields.Int32Value, 3));
        await Error(await context.Raw(HttpMethod.Patch, AssetsTestContext.Path(AssetResources.FieldValue, Id(value)), patch, context.Full), HttpStatusCode.BadRequest);
        Assert.Equal(value.ToString(), (await Read(AssetResources.FieldValue, Id(value))).ToString());
    }

    [Theory]
    [InlineData(DateType, AssetFields.DateValue, "\"2026-02-30\"")]
    [InlineData(BooleanType, AssetFields.BooleanValue, "\"false\"")]
    [InlineData(StringType, AssetFields.StringValue, "42")]
    [InlineData(StringType, AssetFields.StringValue, "\"nul\\u0000text\"")]
    public async Task IncompatibleRepresentationsRemainInputErrors(string fieldType, string slot, string json)
    {
        var type = await Type(); var field = await Define(Id(type), fieldType); var asset = await Asset(Id(type));
        using var document = JsonDocument.Parse(json);
        await Error(await context.Send(HttpMethod.Post, AssetResources.FieldValue, body: Value(Id(asset), Id(field), slot, document.RootElement)), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TypedConstraintsUseTheSharedEvaluatorAndDefinitionErrorsRemain400()
    {
        var type = await Type();
        await Error(await context.Send(HttpMethod.Post, AssetResources.FieldDefinition, body: new { assetTypeId = Id(type), code = "bad", name = "Bad", fieldType = StringType, minimumLength = 5, maximumLength = 2 }), HttpStatusCode.BadRequest);
        await Error(await context.Send(HttpMethod.Post, AssetResources.FieldDefinition, body: new { assetTypeId = Id(type), code = "bad", name = "Bad", fieldType = BooleanType, maximum = 2 }), HttpStatusCode.BadRequest);
        await Error(await context.Send(HttpMethod.Post, AssetResources.FieldDefinition, body: new { assetTypeId = Id(type), code = "bad", name = "Bad", fieldType = IntegerType, maximum = 2147483648L }), HttpStatusCode.BadRequest);
        await Error(await context.Send(HttpMethod.Post, AssetResources.FieldDefinition, body: new { assetTypeId = Id(type), code = "bad", name = "Bad", fieldType = DecimalType, precision = 2, scale = 3 }), HttpStatusCode.BadRequest);
        await Error(await context.Send(HttpMethod.Post, AssetResources.FieldDefinition, body: Definition(Id(type), "UUID", false, true)), HttpStatusCode.BadRequest);
        var text = await context.Create(AssetResources.FieldDefinition, new { assetTypeId = Id(type), code = "text", name = "Text", fieldType = StringType, minimumLength = 2, maximumLength = 3 });
        var integer = await context.Create(AssetResources.FieldDefinition, new { assetTypeId = Id(type), code = "integer", name = "Integer", fieldType = IntegerType, minimum = 1, maximum = 9 });
        var number = await context.Create(AssetResources.FieldDefinition, new { assetTypeId = Id(type), code = "decimal", name = "Decimal", fieldType = DecimalType, precision = 8, scale = 2, minimum = 0, maximum = 100 });
        var asset = await Asset(Id(type));
        await Error(await context.Send(HttpMethod.Post, AssetResources.FieldValue, body: Value(Id(asset), Id(text), AssetFields.StringValue, "A")), HttpStatusCode.BadRequest);
        await Error(await context.Send(HttpMethod.Post, AssetResources.FieldValue, body: Value(Id(asset), Id(text), AssetFields.StringValue, "ABCD")), HttpStatusCode.BadRequest);
        await Error(await context.Send(HttpMethod.Post, AssetResources.FieldValue, body: Value(Id(asset), Id(integer), AssetFields.Int32Value, 0)), HttpStatusCode.BadRequest);
        await Error(await context.Send(HttpMethod.Post, AssetResources.FieldValue, body: Value(Id(asset), Id(number), AssetFields.DecimalValue, 1.234m)), HttpStatusCode.BadRequest);
        var valid = await context.Create(AssetResources.FieldValue, Value(Id(asset), Id(number), AssetFields.DecimalValue, 1.23m));
        Assert.Equal(1.23m, valid.GetProperty(AssetFields.DecimalValue).GetDecimal());
    }

    [Fact]
    public async Task OptionalAndRequiredEvolutionPreservesCompletedAndRetiredAssets()
    {
        var type = await Type(); var asset = await Asset(Id(type));
        using var completed = await context.Action(Id(asset), AssetActions.Complete); Assert.Equal(HttpStatusCode.NoContent, completed.StatusCode);
        var before = await Read(AssetResources.Asset, Id(asset));
        var optional = await Define(Id(type));
        Assert.Equal(before.ToString(), (await Read(AssetResources.Asset, Id(asset))).ToString());
        await Error(await context.Send(HttpMethod.Post, AssetResources.FieldDefinition, body: Definition(Id(type), StringType, true, true)), HttpStatusCode.Conflict, AssetFailures.IncompatibleDefinition);
        await Error(await context.Send(HttpMethod.Patch, AssetResources.FieldDefinition, Id(optional), new { required = true }), HttpStatusCode.Conflict);
        Assert.False((await Read(AssetResources.FieldDefinition, Id(optional))).GetProperty(AssetFields.Required).GetBoolean());
        var value = await context.Create(AssetResources.FieldValue, Value(Id(asset), Id(optional), AssetFields.StringValue, "Filled explicitly"));
        using var promotion = await context.Send(HttpMethod.Patch, AssetResources.FieldDefinition, Id(optional), new { required = true }); Assert.Equal(HttpStatusCode.OK, promotion.StatusCode);
        using var retire = await context.Action(Id(asset), AssetActions.Retire); Assert.Equal(HttpStatusCode.NoContent, retire.StatusCode);
        var staged = await Define(Id(type), required: true, active: false);
        await Error(await context.Send(HttpMethod.Patch, AssetResources.FieldDefinition, Id(staged), new { active = true }), HttpStatusCode.Conflict);
        Assert.Equal(value.ToString(), (await Read(AssetResources.FieldValue, Id(value))).ToString());
        await Error(await context.Send(HttpMethod.Post, AssetResources.FieldValue, body: Value(Id(asset), Id(staged), AssetFields.StringValue, "No backfill bypass")), HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task DisablingPreservesReadableHistoryAndCannotBeUsedToInvalidateOrModifyIt()
    {
        var type = await Type(); var field = await Define(Id(type), IntegerType); var asset = await Asset(Id(type));
        var value = await context.Create(AssetResources.FieldValue, Value(Id(asset), Id(field), AssetFields.Int32Value, 7));
        using var off = await context.Send(HttpMethod.Patch, AssetResources.FieldDefinition, Id(field), new { active = false }); Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        Assert.Equal(value.ToString(), (await Read(AssetResources.FieldValue, Id(value))).ToString());
        await Error(await context.Send(HttpMethod.Patch, AssetResources.FieldValue, Id(value), new { int32Value = 8 }), HttpStatusCode.Conflict, AssetFailures.InactiveField);
        await Error(await context.Send(HttpMethod.Delete, AssetResources.FieldValue, Id(value)), HttpStatusCode.Conflict);
        var other = await Asset(Id(type));
        await Error(await context.Send(HttpMethod.Post, AssetResources.FieldValue, body: Value(Id(other), Id(field), AssetFields.Int32Value, 8)), HttpStatusCode.Conflict);
        await Error(await context.Send(HttpMethod.Patch, AssetResources.FieldDefinition, Id(field), new { maximum = 5 }), HttpStatusCode.Conflict, AssetFailures.IncompatibleDefinition);
        Assert.Equal(JsonValueKind.Null, (await Read(AssetResources.FieldDefinition, Id(field))).GetProperty(AssetFields.Maximum).ValueKind);
        await Error(await context.Send(HttpMethod.Delete, AssetResources.FieldDefinition, Id(field)), HttpStatusCode.Conflict);
        await Error(await context.Send(HttpMethod.Delete, AssetResources.Asset, Id(asset)), HttpStatusCode.Conflict);
        using var on = await context.Send(HttpMethod.Patch, AssetResources.FieldDefinition, Id(field), new { active = true, maximum = 9 }); Assert.Equal(HttpStatusCode.OK, on.StatusCode);
        using var update = await context.Send(HttpMethod.Patch, AssetResources.FieldValue, Id(value), new { int32Value = 9 }); Assert.Equal(HttpStatusCode.OK, update.StatusCode);
    }

    [Fact]
    public async Task InactiveRequiredFieldsDoNotBlockCompletionButReactivationMustCheckExistingState()
    {
        var type = await Type(); var field = await Define(Id(type), BooleanType, required: true); var asset = await Asset(Id(type));
        using var off = await context.Send(HttpMethod.Patch, AssetResources.FieldDefinition, Id(field), new { active = false }); Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        using var complete = await context.Action(Id(asset), AssetActions.Complete); Assert.Equal(HttpStatusCode.NoContent, complete.StatusCode);
        await Error(await context.Send(HttpMethod.Patch, AssetResources.FieldDefinition, Id(field), new { active = true }), HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task DefinitionIdentityCannotReparentOrReinterpretExistingValues()
    {
        var type = await Type(); var field = await Define(Id(type), IntegerType, code: "stable"); var asset = await Asset(Id(type));
        await context.Create(AssetResources.FieldValue, Value(Id(asset), Id(field), AssetFields.Int32Value, 1));
        await Error(await context.Send(HttpMethod.Patch, AssetResources.FieldDefinition, Id(field), new { fieldType = StringType }), HttpStatusCode.Conflict);
        await Error(await context.Send(HttpMethod.Patch, AssetResources.FieldDefinition, Id(field), new { assetTypeId = AssetsSeed.Laptop }), HttpStatusCode.Conflict);
        await Error(await context.Send(HttpMethod.Patch, AssetResources.FieldDefinition, Id(field), new { code = "renamed" }), HttpStatusCode.Conflict);
        await Error(await context.Send(HttpMethod.Post, AssetResources.FieldDefinition, body: Definition(Id(type), StringType, false, true, "stable")), HttpStatusCode.Conflict);
        var second = await Type(); await Define(Id(second), code: "stable");
        Assert.Equal(field.ToString(), (await Read(AssetResources.FieldDefinition, Id(field))).ToString());
        await Error(await context.Send(HttpMethod.Delete, AssetResources.Type, Id(second)), HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ValuesRejectAnotherTypesDefinitionsAndAreUniqueAndImmutableInOwnership()
    {
        var type = await Type(); var other = await Type(); var field = await Define(Id(type)); var foreign = await Define(Id(other)); var asset = await Asset(Id(type));
        await Error(await context.Send(HttpMethod.Post, AssetResources.FieldValue, body: Value(Id(asset), Id(foreign), AssetFields.StringValue, "Foreign")), HttpStatusCode.Conflict, AssetFailures.FieldTypeMismatch);
        var value = await context.Create(AssetResources.FieldValue, Value(Id(asset), Id(field), AssetFields.StringValue, "Own"));
        await Error(await context.Send(HttpMethod.Post, AssetResources.FieldValue, body: Value(Id(asset), Id(field), AssetFields.StringValue, "Duplicate")), HttpStatusCode.Conflict);
        await Error(await context.Send(HttpMethod.Patch, AssetResources.FieldValue, Id(value), new { fieldDefinitionId = Id(foreign) }), HttpStatusCode.Conflict);
        await Error(await context.Send(HttpMethod.Patch, AssetResources.FieldValue, Id(value), new { assetId = Guid.NewGuid() }), HttpStatusCode.Conflict);
        await Error(await context.Send(HttpMethod.Post, AssetResources.FieldValue, body: Value(Id(asset), Guid.NewGuid(), AssetFields.StringValue, "Absent")), HttpStatusCode.NotFound);
        Assert.Equal(value.ToString(), (await Read(AssetResources.FieldValue, Id(value))).ToString());
    }

    [Fact]
    public async Task RetiredAssetsProtectEveryValueMutationWhileKeepingReads()
    {
        var type = await Type(); var field = await Define(Id(type)); var newField = await Define(Id(type)); var asset = await Asset(Id(type));
        var value = await context.Create(AssetResources.FieldValue, Value(Id(asset), Id(field), AssetFields.StringValue, "Historical"));
        using var complete = await context.Action(Id(asset), AssetActions.Complete); Assert.Equal(HttpStatusCode.NoContent, complete.StatusCode);
        using var retire = await context.Action(Id(asset), AssetActions.Retire); Assert.Equal(HttpStatusCode.NoContent, retire.StatusCode);
        var retired = await Read(AssetResources.Asset, Id(asset));
        await Error(await context.Send(HttpMethod.Patch, AssetResources.FieldValue, Id(value), new { stringValue = "Changed" }), HttpStatusCode.Conflict, AssetFailures.Retired);
        await Error(await context.Send(HttpMethod.Delete, AssetResources.FieldValue, Id(value)), HttpStatusCode.Conflict);
        await Error(await context.Send(HttpMethod.Post, AssetResources.FieldValue, body: Value(Id(asset), Id(newField), AssetFields.StringValue, "New")), HttpStatusCode.Conflict);
        Assert.Equal(retired.ToString(), (await Read(AssetResources.Asset, Id(asset))).ToString());
        Assert.Equal(value.ToString(), (await Read(AssetResources.FieldValue, Id(value))).ToString());
        await Error(await context.Send(HttpMethod.Patch, AssetResources.FieldDefinition, Id(field), new { maximumLength = 2 }), HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task IncompatibleConstraintsRejectDraftAndHistoricalValuesWithoutRounding()
    {
        var type = await Type(); var field = await Define(Id(type), DecimalType); var asset = await Asset(Id(type));
        var value = await context.Create(AssetResources.FieldValue, Value(Id(asset), Id(field), AssetFields.DecimalValue, 7.123m));
        await Error(await context.Send(HttpMethod.Patch, AssetResources.FieldDefinition, Id(field), new { scale = 2 }), HttpStatusCode.Conflict);
        await Error(await context.Send(HttpMethod.Patch, AssetResources.FieldDefinition, Id(field), new { maximum = 5 }), HttpStatusCode.Conflict);
        Assert.Equal(field.ToString(), (await Read(AssetResources.FieldDefinition, Id(field))).ToString());
        Assert.Equal(value.ToString(), (await Read(AssetResources.FieldValue, Id(value))).ToString());
    }

    [Fact]
    public async Task SchemaAndMaintenancePermissionsRemainSeparateAndTenantReferencesCannotCross()
    {
        var type = await Type();
        await Error(await context.Send(HttpMethod.Post, AssetResources.FieldDefinition, body: Definition(Id(type), StringType, false, true), token: context.Writer), HttpStatusCode.Forbidden);
        var field = await Define(Id(type), token: context.Schema); var asset = await Asset(Id(type));
        await Error(await context.Send(HttpMethod.Post, AssetResources.FieldValue, body: Value(Id(asset), Id(field), AssetFields.StringValue, "Denied"), token: context.Schema), HttpStatusCode.Forbidden);
        var value = await context.Create(AssetResources.FieldValue, Value(Id(asset), Id(field), AssetFields.StringValue, "Maintained"), context.Writer);
        await Error(await context.Send(HttpMethod.Patch, AssetResources.FieldValue, Id(value), new { stringValue = "Denied" }, token: context.Reader), HttpStatusCode.Forbidden);
        await Error(await context.Send(HttpMethod.Get, AssetResources.FieldValue, Id(value), token: context.NoTenant), HttpStatusCode.Forbidden);
        await Error(await context.Send(HttpMethod.Get, AssetResources.FieldDefinition, Id(field), token: context.OtherTenant), HttpStatusCode.NotFound);
        await Error(await context.Send(HttpMethod.Get, AssetResources.FieldValue, Id(value), token: context.OtherTenant), HttpStatusCode.NotFound);
        var secondType = await Type(context.OtherTenant); var secondField = await Define(Id(secondType), token: context.OtherTenant); var secondAsset = await Asset(Id(secondType), context.OtherTenant);
        await Error(await context.Send(HttpMethod.Post, AssetResources.FieldValue, body: Value(Id(asset), Id(secondField), AssetFields.StringValue, "Cross database")), HttpStatusCode.NotFound);
        await Error(await context.Send(HttpMethod.Post, AssetResources.FieldValue, body: Value(Id(secondAsset), Id(field), AssetFields.StringValue, "Cross database"), token: context.OtherTenant), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MetadataDataAndValuesCanBeDiscoveredWithoutPerFieldSchemaChanges()
    {
        var before = await Columns(); var type = await Type(); var asset = await Asset(Id(type));
        var field = await context.Create(AssetResources.FieldDefinition, new { assetTypeId = Id(type), code = "labelled", name = "Business label", description = "Help", fieldType = BooleanType, required = true, displayOrder = 10 });
        var value = await context.Create(AssetResources.FieldValue, Value(Id(asset), Id(field), AssetFields.BooleanValue, false));
        using var fields = await context.Raw(HttpMethod.Get, AssetsTestContext.Path(AssetResources.FieldDefinition) + "?sort=displayOrder&filter=" + Filter(AssetFields.AssetTypeId, Id(type)), null, context.Reader);
        var definition = Assert.Single((await Body(fields)).GetProperty("items").EnumerateArray());
        Assert.Equal("Business label", definition.GetProperty(AssetFields.Name).GetString()); Assert.Equal(BooleanType, definition.GetProperty(AssetFields.FieldType).GetString());
        Assert.Equal(AssetFields.BooleanValue, definition.GetProperty(AssetFields.ValueField).GetString()); Assert.True(definition.GetProperty(AssetFields.Required).GetBoolean());
        using var values = await context.Raw(HttpMethod.Get, AssetsTestContext.Path(AssetResources.FieldValue) + "?filter=" + Filter(AssetFields.AssetId, Id(asset)), null, context.Reader);
        Assert.Equal(Id(value), Id(Assert.Single((await Body(values)).GetProperty("items").EnumerateArray())));
        Assert.Equal(before, await Columns());
        await using var db = await context.Database(); Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal("jsonb", db.Model.FindEntityType(typeof(AssetFieldValueRow))!.FindProperty(nameof(AssetFieldValueRow.ValueJson))!.GetColumnType());
        Assert.DoesNotContain(db.Model.FindEntityType(typeof(AssetFieldValueRow))!.GetProperties(), property => property.Name is "StringValue" or "Int32Value" or "DecimalValue" or "BooleanValue" or "DateValue");
    }
    private async Task<string[]> Columns()
    {
        await using var db = await context.Database();
        return await db.Database.SqlQuery<string>($"""
            SELECT table_name || '.' || column_name || ':' || data_type AS "Value" FROM information_schema.columns
            WHERE table_schema = current_schema() AND table_name IN ({AssetsStorage.Assets}, {AssetsStorage.FieldDefinitions}, {AssetsStorage.FieldValues})
            ORDER BY table_name, ordinal_position
            """).ToArrayAsync();
    }

    [Fact]
    public async Task PostgresEnforcesTypeOwnershipPairUniquenessAndNoJsonNull()
    {
        var type = await Type(); var otherType = await Type(); var field = await Define(Id(type)); var otherField = await Define(Id(otherType)); var asset = await Asset(Id(type));
        await context.Create(AssetResources.FieldValue, Value(Id(asset), Id(field), AssetFields.StringValue, "Original"));
        await using var db = await context.Database();
        AssetFieldValueRow Row(Guid definition, string json) => new()
        {
            Id = Guid.NewGuid(),
            AssetId = Id(asset),
            AssetTypeId = Id(type),
            FieldDefinitionId = definition,
            ValueJson = json,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.FieldValues.Add(Row(Id(otherField), "\"foreign\""));
        var foreign = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(foreign.InnerException).SqlState); db.ChangeTracker.Clear();
        db.FieldValues.Add(Row(Id(field), "\"duplicate\""));
        var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(duplicate.InnerException).SqlState); db.ChangeTracker.Clear();
        var empty = await Define(Id(type)); db.FieldValues.Add(Row(Id(empty), "null"));
        var nullValue = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(nullValue.InnerException).SqlState);
    }

    [Fact]
    public async Task CompletionRejectsLegacyInvalidRequiredValuesAndDraftRemovalIsExplicit()
    {
        var type = await Type(); var field = await Define(Id(type), IntegerType, required: true); var asset = await Asset(Id(type));
        var value = await context.Create(AssetResources.FieldValue, Value(Id(asset), Id(field), AssetFields.Int32Value, 1));
        await using (var db = await context.Database())
        {
            var row = await db.FieldValues.SingleAsync(row => row.Id == Id(value)); row.ValueJson = "\"not an integer\""; await db.SaveChangesAsync();
        }
        await Error(await context.Action(Id(asset), AssetActions.Complete), HttpStatusCode.Conflict, AssetFailures.RequiredFields);
        // Privileged arrangement of legacy-invalid persisted input is repaired via normal maintenance.
        using var repair = await context.Send(HttpMethod.Patch, AssetResources.FieldValue, Id(value), new { int32Value = 2 });
        // Generic Runtime rejects invalid repository output before PATCH; remove/recreate the DRAFT value instead.
        Assert.Equal(HttpStatusCode.InternalServerError, repair.StatusCode);
        using var remove = await context.Send(HttpMethod.Delete, AssetResources.FieldValue, Id(value)); Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        await Error(await context.Action(Id(asset), AssetActions.Complete), HttpStatusCode.Conflict);
        await context.Create(AssetResources.FieldValue, Value(Id(asset), Id(field), AssetFields.Int32Value, 3));
        using var complete = await context.Action(Id(asset), AssetActions.Complete); Assert.Equal(HttpStatusCode.NoContent, complete.StatusCode);
    }

    [Fact]
    public async Task PersistentDynamicOperationsCanExecuteDirectlyThroughRuntime()
    {
        var type = await Type(); var field = await Define(Id(type), BooleanType, required: true); var asset = await Asset(Id(type));
        await using var scope = context.Host.Services.CreateAsyncScope(); var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, context.FullUser.ToString())], "fixture"))
        };
        try
        {
            var runtime = scope.ServiceProvider.GetRequiredService<ResourceRuntime>();
            var row = await runtime.CreateAsync(AssetResources.FieldValue, new ResourceRecord(new Dictionary<string, ScalarValue>
            { [AssetFields.AssetId] = new UuidValue(Id(asset)), [AssetFields.FieldDefinitionId] = new UuidValue(Id(field)), [AssetFields.BooleanValue] = new BooleanValue(false) }));
            var id = ((UuidValue)row.Values[AssetFields.Id]).Value; context.Track(AssetResources.FieldValue, id);
            var target = new ResourceEntityTarget(new(AssetFields.Id, new UuidValue(Id(asset))));
            await scope.ServiceProvider.GetRequiredService<ResourceActionRuntime>().ExecuteAsync(AssetResources.Asset, AssetActions.Complete, target, new());
            Assert.Equal(new BooleanValue(false), (await runtime.GetAsync(AssetResources.FieldValue, new(AssetFields.Id, new UuidValue(id)))).Values[AssetFields.BooleanValue]);
        }
        finally { accessor.HttpContext = null; }
    }

    [Fact]
    public async Task RequiredEmptyStringIsPresentUnlessItsLengthConstraintForbidsIt()
    {
        var type = await Type(); var field = await Define(Id(type), required: true); var asset = await Asset(Id(type));
        await context.Create(AssetResources.FieldValue, Value(Id(asset), Id(field), AssetFields.StringValue, ""));
        using var complete = await context.Action(Id(asset), AssetActions.Complete); Assert.Equal(HttpStatusCode.NoContent, complete.StatusCode);
        await Error(await context.Send(HttpMethod.Patch, AssetResources.FieldDefinition, Id(field), new { minimumLength = 1 }), HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task OptionalCurrentValueRemovalAndUnusedDefinitionCrudAreExplicit()
    {
        var type = await Type(); var field = await Define(Id(type), BooleanType); var asset = await Asset(Id(type));
        var value = await context.Create(AssetResources.FieldValue, Value(Id(asset), Id(field), AssetFields.BooleanValue, false));
        using var complete = await context.Action(Id(asset), AssetActions.Complete); Assert.Equal(HttpStatusCode.NoContent, complete.StatusCode);
        using var rename = await context.Send(HttpMethod.Patch, AssetResources.FieldDefinition, Id(field), new { name = "Renamed", displayOrder = 2 }); Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
        Assert.Equal(value.ToString(), (await Read(AssetResources.FieldValue, Id(value))).ToString());
        using var remove = await context.Send(HttpMethod.Delete, AssetResources.FieldValue, Id(value)); Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        await Error(await context.Send(HttpMethod.Get, AssetResources.FieldValue, Id(value)), HttpStatusCode.NotFound);
        using var delete = await context.Send(HttpMethod.Delete, AssetResources.FieldDefinition, Id(field)); Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        await Error(await context.Send(HttpMethod.Get, AssetResources.FieldDefinition, Id(field)), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RacesBetweenCompletionEvolutionAndRetirementPreserveCommittedInvariants()
    {
        var type = await Type(); var field = await Define(Id(type)); var asset = await Asset(Id(type));
        var results = await Task.WhenAll(context.Action(Id(asset), AssetActions.Complete), context.Send(HttpMethod.Patch, AssetResources.FieldDefinition, Id(field), new { required = true }));
        try
        {
            Assert.True((results[0].StatusCode == HttpStatusCode.NoContent && results[1].StatusCode == HttpStatusCode.Conflict)
                || (results[0].StatusCode == HttpStatusCode.Conflict && results[1].StatusCode == HttpStatusCode.OK));
        }
        finally { foreach (var result in results) result.Dispose(); }
        var value = await context.Create(AssetResources.FieldValue, Value(Id(asset), Id(field), AssetFields.StringValue, "Value"));
        if ((await Read(AssetResources.Asset, Id(asset))).GetProperty(AssetFields.Lifecycle).GetString() == AssetLifecycle.Draft)
        { using var complete = await context.Action(Id(asset), AssetActions.Complete); Assert.Equal(HttpStatusCode.NoContent, complete.StatusCode); }
        var writes = await Task.WhenAll(context.Action(Id(asset), AssetActions.Retire), context.Send(HttpMethod.Patch, AssetResources.FieldValue, Id(value), new { stringValue = "Before retirement" }));
        try
        {
            Assert.Equal(HttpStatusCode.NoContent, writes[0].StatusCode); Assert.Contains(writes[1].StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
            Assert.Equal(AssetLifecycle.Retired, (await Read(AssetResources.Asset, Id(asset))).GetProperty(AssetFields.Lifecycle).GetString());
        }
        finally { foreach (var result in writes) result.Dispose(); }
    }
}
