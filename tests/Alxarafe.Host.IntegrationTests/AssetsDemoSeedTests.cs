using System.Text.Json;
using Alxarafe.Core;
using Alxarafe.Modules.Assets;
using Alxarafe.Resources;
using Alxarafe.Resources.Runtime;
using Alxarafe.Resources.AspNetCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

// A dedicated real PostgreSQL database per test keeps canonical demo facts out
// of the normal Host fixtures and shared conformance databases.
public sealed class AssetsDemoSeedTests : IAsyncLifetime
{
    private static readonly string[] ExpectedFieldCodes = ["RAM_GB", "ENCRYPTED", "WARRANTY_UNTIL"];
    private static readonly ScalarType[] ExpectedFieldTypes = [ScalarType.Int32, ScalarType.Boolean, ScalarType.Date];
    private const int CanonicalCount = 24;
    private const int LowConditionThreshold = 50;
    private readonly string database = "assets_demo_test_" + Guid.NewGuid().ToString("N");
    private string connection = "", adminConnection = "";
    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("ConnectionStrings__Tenant_development")
            ?? throw new InvalidOperationException("An explicit disposable PostgreSQL connection is required.");
        var builder = new NpgsqlConnectionStringBuilder(configured) { Database = "postgres", Pooling = false };
        adminConnection = builder.ConnectionString;
        await using var admin = new NpgsqlConnection(adminConnection);
        await admin.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin);
        await command.ExecuteNonQueryAsync();
        builder.Database = database; connection = builder.ConnectionString;
    }
    public async Task DisposeAsync()
    {
        await using var admin = new NpgsqlConnection(adminConnection);
        await admin.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)", admin);
        await command.ExecuteNonQueryAsync();
    }
    private AssetsDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AssetsDbContext>();
        AssetsStorage.Configure(options, connection);
        return new(options.Options);
    }
    private async Task Seed(bool? enabled)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(enabled is null ? []
            : new Dictionary<string, string?> { [AssetsDemoSeed.EnabledKey] = enabled.Value.ToString() }).Build();
        await using var services = new ServiceCollection().AddSingleton<IConfiguration>(config)
            .AddSingleton<ITenantDatabaseMigrationRunner>(new SingleDatabase(connection)).BuildServiceProvider();
        await new AssetsModule().InitializeAsync(services);
    }
    private sealed class SingleDatabase(string connection) : ITenantDatabaseMigrationRunner
    {
        public Task MigrateAsync(Func<string, CancellationToken, Task> migrate, CancellationToken cancellationToken = default)
            => migrate(connection, cancellationToken);
    }
    private async Task<string> Snapshot()
    {
        await using var db = Db();
        return JsonSerializer.Serialize(new
        {
            types = await db.Types.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            manufacturers = await db.Manufacturers.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            models = await db.Models.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            locations = await db.Locations.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            statuses = await db.Statuses.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            assets = await db.Assets.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            definitions = await db.FieldDefinitions.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            values = await db.FieldValues.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            assignments = await db.Assignments.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync()
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public async Task MissingOrDisabledFlagKeepsOnlyStructuralSeed(bool? enabled)
    {
        await Seed(enabled); await Seed(enabled);
        await using var db = Db();
        Assert.Equal(3, (await db.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Equal(AssetsSeed.Laptop, (await db.Types.SingleAsync()).Id);
        Assert.Equal(AssetsSeed.Dell, (await db.Manufacturers.SingleAsync()).Id);
        Assert.Equal(AssetsSeed.Latitude, (await db.Models.SingleAsync()).Id);
        Assert.Equal(AssetsSeed.Office, (await db.Locations.SingleAsync()).Id);
        Assert.Equal(AssetStatusCodes.Reserved.Order(), (await db.Statuses.Select(row => row.Code).ToArrayAsync()).Order());
        Assert.Empty(await db.Assets.ToArrayAsync());
        Assert.Empty(await db.FieldDefinitions.ToArrayAsync());
        Assert.Empty(await db.FieldValues.ToArrayAsync());
        Assert.Empty(await db.Assignments.ToArrayAsync());
    }

    [Fact]
    public async Task CanonicalFactsAreComputedFromPostgreSqlWithCoherentReferencesAndDynamicValues()
    {
        await Seed(true);
        await using var db = Db();
        Assert.Equal(CanonicalCount, await db.Assets.CountAsync());
        var types = await (from asset in db.Assets
                           join type in db.Types on asset.AssetTypeId equals type.Id
                           group asset by type.Code into items
                           select new { items.Key, Count = items.Count() }).ToDictionaryAsync(row => row.Key, row => row.Count);
        Assert.Equal(new Dictionary<string, int> { ["LAPTOP"] = 8, ["MONITOR"] = 5, ["PHONE"] = 4, ["PRINTER"] = 3, ["SERVER"] = 4 }.OrderBy(row => row.Key), types.OrderBy(row => row.Key));
        var lifecycles = await db.Assets.GroupBy(row => row.Lifecycle).ToDictionaryAsync(rows => rows.Key, rows => rows.Count());
        Assert.Equal(5, lifecycles[AssetLifecycle.Draft]); Assert.Equal(16, lifecycles[AssetLifecycle.Ready]); Assert.Equal(3, lifecycles[AssetLifecycle.Retired]);
        var makers = await (from asset in db.Assets
                            join maker in db.Manufacturers on asset.ManufacturerId equals maker.Id
                            group asset by maker.Code into items
                            select new { items.Key, Count = items.Count() }).ToDictionaryAsync(row => row.Key, row => row.Count);
        Assert.Equal(new Dictionary<string, int> { ["DELL"] = 6, ["LENOVO"] = 3, ["HP"] = 7, ["SAMSUNG"] = 3, ["APPLE"] = 3 }.OrderBy(row => row.Key), makers.OrderBy(row => row.Key));
        Assert.Equal(2, await db.Assets.CountAsync(row => row.ManufacturerId == null));
        Assert.Equal(6, await db.Assets.CountAsync(row => row.LocationId == null));
        Assert.Equal(6, await db.Assets.CountAsync(row => row.Condition < LowConditionThreshold));
        var repair = await db.Statuses.SingleAsync(row => row.Code == AssetStatusCodes.InRepair);
        Assert.Equal(3, await db.Assets.CountAsync(row => row.StatusId == repair.Id));
        Assert.Equal(5, await db.Types.CountAsync()); Assert.Equal(5, await db.Manufacturers.CountAsync());
        Assert.Equal(13, await db.Models.CountAsync()); Assert.Equal(4, await db.Locations.CountAsync());
        Assert.Equal(AssetStatusCodes.Reserved.Count, await db.Statuses.CountAsync());
        Assert.All(await db.Types.ToArrayAsync(), row => Assert.True(row.Active));
        Assert.All(await db.Manufacturers.ToArrayAsync(), row => Assert.True(row.Active));
        Assert.All(await db.Models.ToArrayAsync(), row => Assert.True(row.Active));
        Assert.All(await db.Locations.ToArrayAsync(), row => Assert.True(row.Active));
        Assert.False(await (from asset in db.Assets
                            join model in db.Models on asset.AssetModelId equals model.Id
                            where asset.ManufacturerId != model.ManufacturerId
                            select asset).AnyAsync());
        var retired = await db.Statuses.SingleAsync(row => row.Code == AssetStatusCodes.Retired);
        Assert.Equal(3, await db.Assets.CountAsync(row => row.Lifecycle == AssetLifecycle.Retired && row.StatusId == retired.Id));
        var fields = await db.FieldDefinitions.OrderBy(row => row.DisplayOrder).ToArrayAsync();
        Assert.Equal(ExpectedFieldCodes, fields.Select(row => row.Code));
        Assert.Equal(ExpectedFieldTypes, fields.Select(row => row.FieldType));
        Assert.Equal("4", fields[0].Minimum); Assert.Equal("128", fields[0].Maximum);
        Assert.True(fields[0].Required); Assert.True(fields[1].Required); Assert.False(fields[2].Required);
        var values = await db.FieldValues.ToArrayAsync();
        Assert.Equal(17, values.Length);
        foreach (var value in values)
        {
            var field = fields.Single(row => row.Id == value.FieldDefinitionId);
            using var json = JsonDocument.Parse(value.ValueJson);
            var scalar = ResourceTransport.Parse(field.FieldType, json.RootElement);
            Assert.True(ResourceValueValidator.IsValid(new FieldDefinition("value", field.FieldType), scalar));
            Assert.Equal(field.AssetTypeId, value.AssetTypeId);
            if (field.Code == "RAM_GB") Assert.InRange(json.RootElement.GetInt32(), 4, 128);
        }
        var laptops = await db.Assets.Where(row => row.AssetTypeId == AssetsSeed.Laptop).ToArrayAsync();
        foreach (var laptop in laptops.Where(row => row.Lifecycle != AssetLifecycle.Draft))
            foreach (var field in fields.Where(row => row.Required)) Assert.Contains(values, row => row.AssetId == laptop.Id && row.FieldDefinitionId == field.Id);
        var drafts = laptops.Where(row => row.Lifecycle == AssetLifecycle.Draft).ToArray();
        Assert.Contains(drafts, asset => values.All(row => row.AssetId != asset.Id));
        Assert.Contains(drafts, asset => values.Count(row => row.AssetId == asset.Id) == 1);
        Assert.Equal(3, await (from value in db.FieldValues
                               join asset in db.Assets on value.AssetId equals asset.Id
                               where value.FieldDefinitionId == fields[0].Id && (value.ValueJson == "8")
                               select asset).CountAsync());
        var assignments = await db.Assignments.ToArrayAsync();
        Assert.Equal(12, assignments.Length); Assert.Equal(8, assignments.Count(row => row.EndedAt == null));
        Assert.Equal(4, assignments.Where(row => row.EndedAt != null).Select(row => row.AssetId).Distinct().Count());
        Assert.All(assignments, row => { Assert.Equal(AssetAssignmentTargets.Location, row.TargetType); Assert.True(row.EndedAt == null || row.EndedAt >= row.StartedAt); });
        foreach (var history in assignments.GroupBy(row => row.AssetId))
        {
            Assert.True(history.Count(row => row.EndedAt == null) <= 1);
            var ordered = history.OrderBy(row => row.StartedAt).ToArray();
            for (int index = 1; index < ordered.Length; index++) Assert.True(ordered[index - 1].EndedAt <= ordered[index].StartedAt);
        }
        Assert.False(await (from assignment in db.Assignments
                            join asset in db.Assets on assignment.AssetId equals asset.Id
                            where assignment.EndedAt == null && asset.Lifecycle != AssetLifecycle.Ready
                            select assignment).AnyAsync());
        var first = await db.Assets.SingleAsync(row => row.AssetTag == "ERB-LT-001");
        Assert.Equal(AssetsSeed.Office, assignments.Single(row => row.AssetId == first.Id && row.EndedAt == null).TargetId);
    }

    [Fact]
    public async Task ReinitializationPreservesExactSnapshotAndForeignDataAndNormalizesOnlyOwnedRows()
    {
        await Seed(true);
        await using (var db = Db())
        {
            db.Assets.Add(new AssetRow
            {
                Id = Guid.NewGuid(),
                AssetTypeId = AssetsSeed.Laptop,
                Name = "User-created draft",
                StatusId = (await db.Statuses.SingleAsync(row => row.Code == AssetStatusCodes.InStock)).Id,
                CreatedAt = new(2025, 8, 1, 0, 0, 0, TimeSpan.Zero),
                UpdatedAt = new(2025, 8, 1, 0, 0, 0, TimeSpan.Zero)
            });
            await db.SaveChangesAsync();
        }
        string first = await Snapshot();
        await Seed(true); Assert.Equal(first, await Snapshot());
        await using (var db = Db())
        {
            var owned = await db.Assets.SingleAsync(row => row.AssetTag == "ERB-LT-001"); owned.Name = "Demo edit"; await db.SaveChangesAsync();
        }
        await Seed(true); Assert.Equal(first, await Snapshot());
    }

    [Fact]
    public async Task NaturalKeyCollisionRollsBackDemoWithoutAdoptingUserCatalogue()
    {
        await Seed(false);
        await using (var db = Db())
        { db.Manufacturers.Add(new() { Id = Guid.NewGuid(), Code = "LENOVO", Name = "User manufacturer", CreatedAt = DateTimeOffset.UnixEpoch, UpdatedAt = DateTimeOffset.UnixEpoch }); await db.SaveChangesAsync(); }
        string before = await Snapshot();
        await Assert.ThrowsAsync<ResourceConflictException>(() => Seed(true));
        Assert.Equal(before, await Snapshot());
    }

    [Fact]
    public async Task RequiredDemoDefinitionsNeverInvalidateExistingCompletedUserLaptops()
    {
        await Seed(false);
        await using (var db = Db())
        { db.Assets.Add(new() { Id = Guid.NewGuid(), AssetTypeId = AssetsSeed.Laptop, Name = "User completed laptop", Lifecycle = AssetLifecycle.Ready, StatusId = (await db.Statuses.SingleAsync(row => row.Code == AssetStatusCodes.Active)).Id, CreatedAt = DateTimeOffset.UnixEpoch, UpdatedAt = DateTimeOffset.UnixEpoch }); await db.SaveChangesAsync(); }
        string before = await Snapshot();
        var failure = await Assert.ThrowsAsync<ResourceConflictException>(() => Seed(true));
        Assert.Equal(AssetFailures.IncompatibleDefinition, failure.ReasonCode);
        Assert.Equal(before, await Snapshot());
    }

    [Fact]
    public async Task UnknownCustodyHistoryIsPreservedAndConflictingReseedRollsBack()
    {
        await Seed(true);
        await using (var db = Db())
        {
            var asset = await db.Assets.SingleAsync(row => row.AssetTag == "ERB-LT-001");
            var current = await db.Assignments.SingleAsync(row => row.AssetId == asset.Id && row.EndedAt == null);
            var transition = new DateTimeOffset(2025, 3, 1, 0, 0, 0, TimeSpan.Zero);
            current.EndedAt = transition; current.EndReason = "User transfer"; await db.SaveChangesAsync();
            db.Assignments.Add(new() { Id = Guid.NewGuid(), AssetId = asset.Id, TargetId = AssetsSeed.Office, StartedAt = transition, CreatedAt = transition, UpdatedAt = transition }); await db.SaveChangesAsync();
        }
        string before = await Snapshot();
        await Assert.ThrowsAsync<ResourceConflictException>(() => Seed(true));
        Assert.Equal(before, await Snapshot());
    }

    [Fact]
    public async Task RealDatabaseConstraintsRemainAuthoritativeAfterDemoSeed()
    {
        await Seed(true); await using var db = Db();
        var asset = await db.Assets.FirstAsync();
        var range = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            $"UPDATE {AssetsStorage.Assets} SET \"{AssetFields.Condition}\" = {{0}} WHERE {AssetFields.Id} = {{1}}", AssetLimits.MaximumCondition + 1, asset.Id));
        Assert.Equal(PostgresErrorCodes.CheckViolation, range.SqlState);
        var assignment = await db.Assignments.FirstAsync(row => row.EndedAt == null);
        var period = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            $"UPDATE {AssetsStorage.Assignments} SET \"{AssetFields.EndedAt}\" = \"{AssetFields.StartedAt}\" - interval '1 day', \"{AssetFields.EndReason}\" = 'invalid' WHERE {AssetFields.Id} = {{0}}", assignment.Id));
        Assert.Equal(PostgresErrorCodes.CheckViolation, period.SqlState);
        var fieldValue = await db.FieldValues.FirstAsync();
        var otherType = await db.Types.FirstAsync(row => row.Id != AssetsSeed.Laptop);
        var reference = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            $"UPDATE {AssetsStorage.FieldValues} SET \"{AssetFields.AssetTypeId}\" = {{0}} WHERE {AssetFields.Id} = {{1}}", otherType.Id, fieldValue.Id));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, reference.SqlState);
        Assert.Equal(CanonicalCount, await db.Assets.CountAsync());
    }
}
