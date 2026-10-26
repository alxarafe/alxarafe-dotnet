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
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class AssetAssignmentTests(AssetsTestContext context) : IClassFixture<AssetsTestContext>
{
    private const string ClosureReason = "Returned to custody pool";
    private const string TransferReason = "Custody handover";
    private const string RollbackMarker = "assignment-rollback-probe";
    private static Guid Id(JsonElement row) => row.GetProperty(AssetFields.Id).GetGuid();
    private static async Task<JsonElement> Body(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();
    private async Task<Guid> Ready(string? token = null)
    {
        var asset = await context.Asset(token: token); using var complete = await context.Action(Id(asset), AssetActions.Complete, token);
        Assert.Equal(HttpStatusCode.NoContent, complete.StatusCode); return Id(asset);
    }
    private async Task<Guid> Location(string? token = null)
    {
        var location = await context.Create(AssetResources.Location, new { code = Guid.NewGuid().ToString("N")[..AssetLimits.CodeLength], name = "Custodian location" }, token);
        return Id(location);
    }
    private Task<HttpResponseMessage> Assign(Guid asset, Guid target, string? token = null, string? notes = null) => context.Action(asset, AssetActions.Assign, token,
        new { targetType = AssetAssignmentTargets.Location, targetId = target, reason = "Initial custody", notes = notes ?? "Custody note" });
    private Task<HttpResponseMessage> Unassign(Guid asset, string? token = null) => context.Action(asset, AssetActions.Unassign, token, new { reason = ClosureReason });
    private Task<HttpResponseMessage> Transfer(Guid asset, Guid target, string? token = null, string? notes = null) => context.Action(asset, AssetActions.Transfer, token,
        new { targetType = AssetAssignmentTargets.Location, targetId = target, reason = TransferReason, notes = notes ?? "Handover note" });
    private async Task<JsonElement> Get(string resource, Guid id, string? token = null)
    {
        using var response = await context.Send(HttpMethod.Get, resource, id, token: token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); return await Body(response);
    }
    private static string Filter(string field, string op, object? value) => Uri.EscapeDataString(JsonSerializer.Serialize(new { field, @operator = op, value }));
    private async Task<JsonElement[]> History(Guid asset, bool current = false, string? token = null)
    {
        string path = AssetsTestContext.Path(AssetResources.Assignment) + "?sort=startedAt&filter=" + Filter(AssetFields.AssetId, "eq", asset);
        if (current) path += "&filter=" + Filter(AssetFields.EndedAt, "eq", null);
        using var response = await context.Raw(HttpMethod.Get, path, null, token ?? context.Full);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await Body(response)).GetProperty("items").EnumerateArray().ToArray();
    }
    private static async Task Error(HttpResponseMessage response, HttpStatusCode status, string? reason = null)
    {
        using (response)
        {
            Assert.Equal(status, response.StatusCode); var body = await Body(response);
            if (status == HttpStatusCode.BadRequest) Assert.Equal(ResourceEndpoints.InvalidRequest, body.GetProperty("code").GetString());
            if (status == HttpStatusCode.Conflict) Assert.Equal("resource_action_conflict", body.GetProperty("code").GetString());
            if (reason is not null) Assert.Equal(reason, body.GetProperty("errors")[0].GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task AssignmentCreatesCanonicalCurrentHistoryWithoutChangingPhysicalLocationOrStatus()
    {
        var asset = await Ready(); var target = await Location();
        using var baseLocation = await context.Send(HttpMethod.Patch, AssetResources.Asset, asset, new { locationId = AssetsSeed.Office }); Assert.Equal(HttpStatusCode.OK, baseLocation.StatusCode);
        var before = await Get(AssetResources.Asset, asset); var start = DateTimeOffset.UtcNow;
        using var assigned = await Assign(asset, target, context.Custody); Assert.Equal(HttpStatusCode.NoContent, assigned.StatusCode);
        var current = Assert.Single(await History(asset, current: true));
        Assert.Equal(asset, current.GetProperty(AssetFields.AssetId).GetGuid()); Assert.Equal(target, current.GetProperty(AssetFields.TargetId).GetGuid());
        Assert.Equal(AssetAssignmentTargets.Location, current.GetProperty(AssetFields.TargetType).GetString()); Assert.Equal(AssetResources.Location, current.GetProperty(AssetFields.TargetResourceId).GetString());
        Assert.Equal(DevelopmentTenantBootstrap.TenantId, current.GetProperty(AssetFields.TenantId).GetString());
        Assert.Equal(JsonValueKind.Null, current.GetProperty(AssetFields.EndedAt).ValueKind);
        Assert.InRange(current.GetProperty(AssetFields.StartedAt).GetDateTimeOffset(), start.AddMilliseconds(-1), DateTimeOffset.UtcNow);
        var after = await Get(AssetResources.Asset, asset);
        Assert.Equal(before.GetProperty(AssetFields.LocationId).GetGuid(), after.GetProperty(AssetFields.LocationId).GetGuid());
        Assert.Equal(before.GetProperty(AssetFields.StatusId).GetGuid(), after.GetProperty(AssetFields.StatusId).GetGuid());
        Assert.False(after.TryGetProperty("assignedTo", out _));
        await Error(await Assign(asset, target), HttpStatusCode.Conflict, AssetFailures.AlreadyAssigned);
        Assert.Equal(current.ToString(), (await Get(AssetResources.Assignment, Id(current))).ToString());
    }

    [Fact]
    public async Task DraftAndRetiredAssetsCannotReceiveOrTransferCustody()
    {
        var draft = await context.Asset(); var target = await Location();
        await Error(await Assign(Id(draft), target), HttpStatusCode.Conflict, AssetFailures.InvalidTransition);
        var asset = await Ready(); using var retire = await context.Action(asset, AssetActions.Retire); Assert.Equal(HttpStatusCode.NoContent, retire.StatusCode);
        await Error(await Assign(asset, target), HttpStatusCode.Conflict, AssetFailures.Retired);
        await Error(await Transfer(asset, target), HttpStatusCode.Conflict, AssetFailures.Retired);
        Assert.Empty(await History(asset));
    }

    [Fact]
    public async Task UnassignmentClosesAndPreservesTheRowAndRequiresAnExistingCurrentAssignment()
    {
        var asset = await Ready(); var target = await Location();
        await Error(await Unassign(asset), HttpStatusCode.Conflict, AssetFailures.NotAssigned);
        using var assigned = await Assign(asset, target); Assert.Equal(HttpStatusCode.NoContent, assigned.StatusCode);
        var before = Assert.Single(await History(asset));
        using var closed = await Unassign(asset, context.Custody); Assert.Equal(HttpStatusCode.NoContent, closed.StatusCode);
        Assert.Empty(await History(asset, current: true)); var history = Assert.Single(await History(asset));
        Assert.Equal(Id(before), Id(history)); Assert.Equal(ClosureReason, history.GetProperty(AssetFields.EndReason).GetString());
        Assert.True(history.GetProperty(AssetFields.EndedAt).GetDateTimeOffset() >= history.GetProperty(AssetFields.StartedAt).GetDateTimeOffset());
        Assert.Equal(before.GetProperty(AssetFields.CreatedAt).GetString(), history.GetProperty(AssetFields.CreatedAt).GetString());
        await Error(await Unassign(asset), HttpStatusCode.Conflict, AssetFailures.NotAssigned);
        using var reassigned = await Assign(asset, target); Assert.Equal(HttpStatusCode.NoContent, reassigned.StatusCode);
        Assert.Equal(2, (await History(asset)).Length); Assert.Single(await History(asset, current: true));
    }

    [Fact]
    public async Task TransferClosesOldAndStartsNewAtExactlyTheSameBoundary()
    {
        var asset = await Ready(); var first = await Location(); var second = await Location();
        await Error(await Transfer(asset, second), HttpStatusCode.Conflict, AssetFailures.NotAssigned);
        using var assign = await Assign(asset, first); Assert.Equal(HttpStatusCode.NoContent, assign.StatusCode);
        var old = Assert.Single(await History(asset));
        await Error(await Transfer(asset, first), HttpStatusCode.Conflict, AssetFailures.SameAssignmentTarget);
        using var transfer = await Transfer(asset, second, context.Custody); Assert.Equal(HttpStatusCode.NoContent, transfer.StatusCode);
        var rows = await History(asset); Assert.Equal(2, rows.Length); var current = Assert.Single(await History(asset, current: true));
        var previous = await Get(AssetResources.Assignment, Id(old));
        Assert.Equal(first, previous.GetProperty(AssetFields.TargetId).GetGuid()); Assert.Equal(second, current.GetProperty(AssetFields.TargetId).GetGuid());
        Assert.Equal(previous.GetProperty(AssetFields.EndedAt).GetDateTimeOffset(), current.GetProperty(AssetFields.StartedAt).GetDateTimeOffset());
        Assert.Equal(TransferReason, previous.GetProperty(AssetFields.EndReason).GetString()); Assert.Equal(TransferReason, current.GetProperty(AssetFields.StartReason).GetString());
        Assert.NotEqual(Id(previous), Id(current));
    }

    [Fact]
    public async Task TransferFailureDoesNotCloseTheOldAssignment()
    {
        var asset = await Ready(); var first = await Location(); var second = await Location();
        using var assigned = await Assign(asset, first); Assert.Equal(HttpStatusCode.NoContent, assigned.StatusCode);
        var current = Assert.Single(await History(asset)); var assetBefore = await Get(AssetResources.Asset, asset);
        await Error(await Transfer(asset, Guid.NewGuid()), HttpStatusCode.NotFound);
        Assert.Equal(current.ToString(), (await Get(AssetResources.Assignment, Id(current))).ToString());
        // Inject a PostgreSQL failure on the successor INSERT, after the handler
        // has flushed the old row's closure. The enclosing transaction must roll back.
        string function = "assignment_fail_" + Guid.NewGuid().ToString("N");
        await using var db = await context.Database(); await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"""
            CREATE FUNCTION "{function}"() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
                IF NEW.{AssetFields.Notes} = '{RollbackMarker}' THEN
                    RAISE EXCEPTION 'assignment fixture failure' USING ERRCODE = '23514';
                END IF;
                RETURN NEW;
            END $$;
            CREATE TRIGGER "{function}" BEFORE INSERT ON {AssetsStorage.Assignments} FOR EACH ROW EXECUTE FUNCTION "{function}"();
            """;
        await command.ExecuteNonQueryAsync();
        try
        {
            using var response = await Transfer(asset, second, notes: RollbackMarker); Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain("fixture failure", body, StringComparison.Ordinal);
            Assert.Equal(current.ToString(), (await Get(AssetResources.Assignment, Id(current))).ToString());
            Assert.Single(await History(asset)); Assert.Equal(assetBefore.ToString(), (await Get(AssetResources.Asset, asset)).ToString());
        }
        finally
        {
            command.CommandText = $"""DROP TRIGGER "{function}" ON {AssetsStorage.Assignments}; DROP FUNCTION "{function}"();""";
            await command.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task RetirementRequiresExplicitUnassignmentAndKeepsHistory()
    {
        var asset = await Ready(); var target = await Location(); using var assigned = await Assign(asset, target); Assert.Equal(HttpStatusCode.NoContent, assigned.StatusCode);
        var current = Assert.Single(await History(asset));
        await Error(await context.Action(asset, AssetActions.Retire), HttpStatusCode.Conflict, AssetFailures.AlreadyAssigned);
        Assert.Equal(current.ToString(), (await Get(AssetResources.Assignment, Id(current))).ToString());
        using var unassigned = await Unassign(asset); Assert.Equal(HttpStatusCode.NoContent, unassigned.StatusCode);
        var closed = Assert.Single(await History(asset));
        using var retired = await context.Action(asset, AssetActions.Retire); Assert.Equal(HttpStatusCode.NoContent, retired.StatusCode);
        Assert.Equal(AssetLifecycle.Retired, (await Get(AssetResources.Asset, asset)).GetProperty(AssetFields.Lifecycle).GetString());
        Assert.Equal(closed.ToString(), (await Get(AssetResources.Assignment, Id(closed))).ToString()); Assert.Empty(await History(asset, current: true));
    }

    [Fact]
    public async Task InactiveTargetsAndReferencedMastersCannotEraseHistory()
    {
        var asset = await Ready(); var target = await Location(); using var assigned = await Assign(asset, target); Assert.Equal(HttpStatusCode.NoContent, assigned.StatusCode);
        using var disabled = await context.Send(HttpMethod.Patch, AssetResources.Location, target, new { active = false }); Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        var current = Assert.Single(await History(asset)); Assert.Equal(target, current.GetProperty(AssetFields.TargetId).GetGuid());
        var other = await Ready(); await Error(await Assign(other, target), HttpStatusCode.Conflict, AssetFailures.Inactive);
        using var unassigned = await Unassign(asset); Assert.Equal(HttpStatusCode.NoContent, unassigned.StatusCode);
        var closed = Assert.Single(await History(asset));
        using var targetDelete = await context.Send(HttpMethod.Delete, AssetResources.Location, target); Assert.Equal(HttpStatusCode.Conflict, targetDelete.StatusCode);
        using var assetDelete = await context.Send(HttpMethod.Delete, AssetResources.Asset, asset); Assert.Equal(HttpStatusCode.Conflict, assetDelete.StatusCode);
        Assert.Equal(closed.ToString(), (await Get(AssetResources.Assignment, Id(closed))).ToString());
        await using var db = await context.Database(); db.Locations.Remove(await db.Locations.SingleAsync(row => row.Id == target));
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(failure.InnerException).SqlState);
    }

    [Fact]
    public async Task CustodyPermissionIsIndependentOfMaintenanceSchemaAndSensitiveRights()
    {
        var asset = await Ready(); var target = await Location();
        foreach (string token in new[] { context.Writer, context.Reader, context.Schema, context.SensitiveWithoutMaintain, context.MaintainWithoutRead })
            await Error(await Assign(asset, target, token), HttpStatusCode.Forbidden);
        using var permitted = await Assign(asset, target, context.Custody); Assert.Equal(HttpStatusCode.NoContent, permitted.StatusCode);
        using var maintenance = await context.Send(HttpMethod.Patch, AssetResources.Asset, asset, new { name = "No maintenance grant" }, context.Custody); Assert.Equal(HttpStatusCode.Forbidden, maintenance.StatusCode);
        await Error(await context.Action(asset, AssetActions.Retire, context.Custody), HttpStatusCode.Forbidden);
        var current = Assert.Single(await History(asset));
        using var denied = await context.Send(HttpMethod.Get, AssetResources.Assignment, Id(current), token: context.NoPermission); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var close = await Unassign(asset, context.Custody); Assert.Equal(HttpStatusCode.NoContent, close.StatusCode);
    }

    [Fact]
    public async Task TenantIsolationIncludesTargetsHistoryAndAuthoritativeProjection()
    {
        var asset = await Ready(); var primaryTarget = await Location(); var foreignTarget = await Location(context.OtherTenant);
        await Error(await Assign(asset, foreignTarget), HttpStatusCode.NotFound);
        await Error(await Assign(asset, primaryTarget, context.OtherTenant), HttpStatusCode.NotFound);
        await Error(await Assign(asset, primaryTarget, context.NoTenant), HttpStatusCode.Forbidden);
        using var assign = await Assign(asset, primaryTarget); Assert.Equal(HttpStatusCode.NoContent, assign.StatusCode);
        var current = Assert.Single(await History(asset));
        using var cross = await context.Send(HttpMethod.Get, AssetResources.Assignment, Id(current), token: context.OtherTenant); Assert.Equal(HttpStatusCode.NotFound, cross.StatusCode);
        Assert.Empty(await History(asset, token: context.OtherTenant));
        var other = await Ready(context.OtherTenant); using var foreignAssign = await Assign(other, foreignTarget, context.OtherTenant); Assert.Equal(HttpStatusCode.NoContent, foreignAssign.StatusCode);
        var foreign = Assert.Single(await History(other, token: context.OtherTenant));
        Assert.NotEqual(current.GetProperty(AssetFields.TenantId).GetString(), foreign.GetProperty(AssetFields.TenantId).GetString());
        Assert.Empty(await History(other));
        using var noTenant = await context.Send(HttpMethod.Get, AssetResources.Assignment, Id(current), token: context.NoTenant); Assert.Equal(HttpStatusCode.Forbidden, noTenant.StatusCode);
    }

    [Fact]
    public async Task HistoryCrudCannotChangeOwnerReopenCloseOrEraseAssignments()
    {
        var asset = await Ready(); var target = await Location(); using var assigned = await Assign(asset, target); Assert.Equal(HttpStatusCode.NoContent, assigned.StatusCode);
        var row = Assert.Single(await History(asset));
        using var create = await context.Send(HttpMethod.Post, AssetResources.Assignment, body: new { assetId = asset, targetType = AssetAssignmentTargets.Location, targetId = target, startedAt = DateTimeOffset.UtcNow, endedAt = DateTimeOffset.UtcNow.AddDays(-1) });
        Assert.Equal(HttpStatusCode.MethodNotAllowed, create.StatusCode);
        foreach (object patch in new object[] { new { assetId = Guid.NewGuid() }, new { endedAt = DateTimeOffset.UtcNow }, new { targetId = Guid.NewGuid() } })
        { using var response = await context.Send(HttpMethod.Patch, AssetResources.Assignment, Id(row), patch); Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode); }
        using var close = await Unassign(asset); Assert.Equal(HttpStatusCode.NoContent, close.StatusCode); var history = Assert.Single(await History(asset));
        using var reopen = await context.Send(HttpMethod.Patch, AssetResources.Assignment, Id(row), new { endedAt = (DateTimeOffset?)null }); Assert.Equal(HttpStatusCode.MethodNotAllowed, reopen.StatusCode);
        using var delete = await context.Send(HttpMethod.Delete, AssetResources.Assignment, Id(row)); Assert.Equal(HttpStatusCode.MethodNotAllowed, delete.StatusCode);
        Assert.Equal(history.ToString(), (await Get(AssetResources.Assignment, Id(row))).ToString());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"targetType\":\"PERSON\",\"targetId\":\"130e9201-e5a9-4739-8be0-000000000004\"}")]
    [InlineData("{\"targetType\":\"LOCATION\",\"targetId\":\"invalid\"}")]
    [InlineData("{\"targetType\":\"LOCATION\",\"targetId\":\"130e9201-e5a9-4739-8be0-000000000004\",\"reason\":\"   \"}")]
    [InlineData("{\"targetType\":\"LOCATION\",\"targetId\":\"130e9201-e5a9-4739-8be0-000000000004\",\"startedAt\":\"2020-01-01T00:00:00Z\"}")]
    public async Task InvalidActionInputRemains400AndCannotBackdate(string parameters)
    {
        var asset = await Ready(); using var json = JsonDocument.Parse(parameters);
        await Error(await context.Action(asset, AssetActions.Assign, parameters: json.RootElement), HttpStatusCode.BadRequest);
        Assert.Empty(await History(asset));
    }

    [Fact]
    public async Task ClosureReasonIsRequiredAndMissingTargetsRemain404()
    {
        var asset = await Ready(); var target = await Location(); using var assign = await Assign(asset, target); Assert.Equal(HttpStatusCode.NoContent, assign.StatusCode);
        var row = Assert.Single(await History(asset));
        await Error(await context.Action(asset, AssetActions.Unassign), HttpStatusCode.BadRequest);
        await Error(await context.Action(asset, AssetActions.Unassign, parameters: new { reason = " " }), HttpStatusCode.BadRequest);
        await Error(await context.Action(asset, AssetActions.Transfer, parameters: new { targetType = AssetAssignmentTargets.Location, targetId = target }), HttpStatusCode.BadRequest);
        await Error(await Assign(Guid.NewGuid(), target), HttpStatusCode.NotFound);
        Assert.Equal(row.ToString(), (await Get(AssetResources.Assignment, Id(row))).ToString());
    }

    [Fact]
    public async Task ConcurrentAssignAndTransferAttemptsPreserveOneCurrentAssignment()
    {
        var asset = await Ready(); var first = await Location(); var second = await Location(); var third = await Location();
        var assigns = await Task.WhenAll(Assign(asset, first), Assign(asset, second));
        try { Assert.Equal(new[] { HttpStatusCode.NoContent, HttpStatusCode.Conflict }, assigns.Select(response => response.StatusCode).OrderBy(status => (int)status)); }
        finally { foreach (var response in assigns) response.Dispose(); }
        var current = Assert.Single(await History(asset, current: true));
        Guid alternate = current.GetProperty(AssetFields.TargetId).GetGuid() == first ? second : first;
        var transfers = await Task.WhenAll(Transfer(asset, alternate), Transfer(asset, third));
        try { Assert.All(transfers, response => Assert.Equal(HttpStatusCode.NoContent, response.StatusCode)); }
        finally { foreach (var response in transfers) response.Dispose(); }
        Assert.Single(await History(asset, current: true)); Assert.Equal(3, (await History(asset)).Length);
        var history = await History(asset);
        foreach (var row in history.Where(row => row.GetProperty(AssetFields.EndedAt).ValueKind != JsonValueKind.Null))
            Assert.True(row.GetProperty(AssetFields.EndedAt).GetDateTimeOffset() >= row.GetProperty(AssetFields.StartedAt).GetDateTimeOffset());
    }

    private static AssetAssignmentRow Native(Guid asset, Guid target, DateTimeOffset start, DateTimeOffset? end = null) => new()
    {
        Id = Guid.NewGuid(),
        AssetId = asset,
        TargetId = target,
        TargetType = AssetAssignmentTargets.Location,
        StartedAt = start,
        EndedAt = end,
        EndReason = end is null ? null : ClosureReason,
        CreatedAt = start,
        UpdatedAt = end ?? start
    };
    [Fact]
    public async Task PostgresRejectsDuplicateCurrentOverlapNegativePeriodsAndMissingReferences()
    {
        var asset = await Ready(); var target = await Location(); var start = DateTimeOffset.UtcNow.AddDays(-2);
        await using var db = await context.Database();
        db.Assignments.Add(Native(asset, target, start, start.AddHours(1))); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        db.Assignments.Add(Native(asset, target, start.AddMinutes(30), start.AddHours(2)));
        var overlap = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); Assert.Equal(PostgresErrorCodes.ExclusionViolation, Assert.IsType<PostgresException>(overlap.InnerException).SqlState); db.ChangeTracker.Clear();
        db.Assignments.Add(Native(asset, target, start.AddHours(1), start.AddHours(2))); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        db.Assignments.Add(Native(asset, target, start.AddHours(2))); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        db.Assignments.Add(Native(asset, target, start.AddHours(3)));
        var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); Assert.Contains(Assert.IsType<PostgresException>(duplicate.InnerException).SqlState, new[] { PostgresErrorCodes.UniqueViolation, PostgresErrorCodes.ExclusionViolation }); db.ChangeTracker.Clear();
        var other = await Ready(); db.Assignments.Add(Native(other, target, start, start.AddMinutes(-1)));
        var negative = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(negative.InnerException).SqlState); db.ChangeTracker.Clear();
        db.Assignments.Add(Native(other, Guid.NewGuid(), start));
        var missing = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(missing.InnerException).SqlState); db.ChangeTracker.Clear();
        var index = await db.Database.SqlQuery<string>($"""SELECT indexdef AS "Value" FROM pg_indexes WHERE schemaname = current_schema() AND indexname = {AssetsStorage.CurrentAssignmentIndex}""").SingleAsync();
        Assert.Contains("UNIQUE", index, StringComparison.Ordinal); Assert.Contains(AssetFields.EndedAt, index, StringComparison.Ordinal);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task ClockEarlierThanRecordedHistoryCannotCreateIncoherentChronology()
    {
        var asset = await Ready(); var target = await Location();
        await using (var db = await context.Database()) { db.Assignments.Add(Native(asset, target, DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(2))); await db.SaveChangesAsync(); }
        await Error(await Assign(asset, target), HttpStatusCode.Conflict, AssetFailures.AssignmentChronology);
        var other = await Ready();
        await using (var db = await context.Database()) { db.Assignments.Add(Native(other, target, DateTimeOffset.UtcNow.AddDays(1))); await db.SaveChangesAsync(); }
        await Error(await Unassign(other), HttpStatusCode.Conflict, AssetFailures.AssignmentChronology);
    }

    [Fact]
    public async Task GenericRuntimeMetadataAndTemporalQueriesExposeHistoryWithoutMutationCapabilities()
    {
        var asset = await Ready(); var target = await Location(); using var assign = await Assign(asset, target); Assert.Equal(HttpStatusCode.NoContent, assign.StatusCode);
        var row = Assert.Single(await History(asset));
        using var metadata = await context.Raw(HttpMethod.Get, ResourceEndpoints.MetadataPath + "/" + AssetResources.Assignment, null, context.Reader);
        var meta = await Body(metadata); Assert.False(meta.GetProperty("operations").GetProperty("create").GetProperty("declared").GetBoolean());
        Assert.All(meta.GetProperty("fields").EnumerateArray(), field => Assert.True(field.GetProperty("readOnly").GetBoolean()));
        string path = AssetsTestContext.Path(AssetResources.Assignment) + "?filter=" + Filter(AssetFields.AssetId, "eq", asset)
            + "&filter=" + Filter(AssetFields.StartedAt, "gte", "2000-01-01T02:00:00+02:00");
        using var temporal = await context.Raw(HttpMethod.Get, path, null, context.Reader); Assert.Equal(HttpStatusCode.OK, temporal.StatusCode);
        Assert.Single((await Body(temporal)).GetProperty("items").EnumerateArray());
        await using var scope = context.Host.Services.CreateAsyncScope(); var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider, User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, context.FullUser.ToString())], "fixture")) };
        try
        {
            var runtime = scope.ServiceProvider.GetRequiredService<ResourceRuntime>(); var identifier = new ResourceIdentifier(AssetFields.Id, new UuidValue(Id(row)));
            Assert.Equal(new UuidValue(asset), (await runtime.GetAsync(AssetResources.Assignment, identifier)).Values[AssetFields.AssetId]);
            var failure = await Assert.ThrowsAsync<ResourceRuntimeException>(() => runtime.UpdateAsync(AssetResources.Assignment, identifier, new ResourceRecord([])));
            Assert.Equal(ResourceRuntimeError.OperationNotSupported, failure.Code);
            await scope.ServiceProvider.GetRequiredService<ResourceActionRuntime>().ExecuteAsync(AssetResources.Asset, AssetActions.Unassign,
                new ResourceEntityTarget(new(AssetFields.Id, new UuidValue(asset))), new(new Dictionary<string, ScalarValue> { [AssetFields.Reason] = new TextValue(ClosureReason) }));
        }
        finally { accessor.HttpContext = null; }
        Assert.Empty(await History(asset, current: true));
    }

    [Fact]
    public async Task AssignmentDoesNotReplaceOrAlterDynamicProperties()
    {
        var asset = await Ready(); var target = await Location();
        var field = await context.Create(AssetResources.FieldDefinition, new { assetTypeId = AssetsSeed.Laptop, code = "custody_" + Guid.NewGuid().ToString("N")[..12], name = "Property", fieldType = "STRING" });
        var value = await context.Create(AssetResources.FieldValue, new { assetId = asset, fieldDefinitionId = Id(field), stringValue = "Property survives custody" });
        using var assigned = await Assign(asset, target); Assert.Equal(HttpStatusCode.NoContent, assigned.StatusCode);
        Assert.Equal(value.ToString(), (await Get(AssetResources.FieldValue, Id(value))).ToString());
        using var unassigned = await Unassign(asset); Assert.Equal(HttpStatusCode.NoContent, unassigned.StatusCode);
        using var retired = await context.Action(asset, AssetActions.Retire); Assert.Equal(HttpStatusCode.NoContent, retired.StatusCode);
        Assert.Equal(value.ToString(), (await Get(AssetResources.FieldValue, Id(value))).ToString());
    }
}
