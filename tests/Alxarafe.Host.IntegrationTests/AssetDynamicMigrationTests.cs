using System.Reflection;
using Alxarafe.Modules.Assets;
using Alxarafe.Modules.Assets.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class AssetDynamicMigrationTests(AssetsTestContext context) : IClassFixture<AssetsTestContext>
{
    [Fact]
    public async Task IncrementalUpgradeAndRollbackPreserveExistingStaticAssets()
    {
        await using var current = await context.Database();
        var connection = new NpgsqlConnectionStringBuilder(current.Database.GetConnectionString());
        string schema = "assets_upgrade_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(connection.ConnectionString); await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin)) await create.ExecuteNonQueryAsync();
        connection.SearchPath = schema;
        var options = new DbContextOptionsBuilder<AssetsDbContext>(); AssetsStorage.Configure(options, connection.ConnectionString);
        try
        {
            await using var db = new AssetsDbContext(options.Options);
            string initial = db.GetService<IMigrationsIdGenerator>().GetName(typeof(InitialAssets).GetCustomAttribute<MigrationAttribute>()!.Id);
            await db.GetService<IMigrator>().MigrateAsync(initial);
            var type = new AssetTypeRow { Id = Guid.NewGuid(), Code = "LEGACY", Name = "Legacy type", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
            var status = new AssetStatusRow { Id = Guid.NewGuid(), Code = AssetStatusCodes.InStock, Name = "Stock", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
            db.Types.Add(type); db.Statuses.Add(status); await db.SaveChangesAsync();
            var asset = new AssetRow { Id = Guid.NewGuid(), Name = "Existing asset", AssetTypeId = type.Id, StatusId = status.Id, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
            db.Assets.Add(asset); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            await db.Database.MigrateAsync(); Assert.Empty(await db.FieldDefinitions.ToArrayAsync()); Assert.Empty(await db.FieldValues.ToArrayAsync());
            Assert.Equal(asset.Name, (await db.Assets.SingleAsync()).Name); Assert.False(db.Database.HasPendingModelChanges());
            await db.GetService<IMigrator>().MigrateAsync(initial); db.ChangeTracker.Clear();
            Assert.Equal(asset.Id, (await db.Assets.SingleAsync()).Id);
            await db.Database.MigrateAsync(); Assert.Equal(3, (await db.Database.GetAppliedMigrationsAsync()).Count());
            var location = new AssetLocationRow { Id = Guid.NewGuid(), Code = "CUSTODY", Name = "Historical target", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
            db.Locations.Add(location); await db.SaveChangesAsync();
            var assignment = new AssetAssignmentRow
            {
                Id = Guid.NewGuid(),
                AssetId = asset.Id,
                TargetId = location.Id,
                StartedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.Assignments.Add(assignment); await db.SaveChangesAsync();
            await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync(initial));
            db.ChangeTracker.Clear(); Assert.Equal(assignment.Id, (await db.Assignments.SingleAsync()).Id);
            Assert.Equal(3, (await db.Database.GetAppliedMigrationsAsync()).Count());
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", admin); await drop.ExecuteNonQueryAsync();
        }
    }
}
