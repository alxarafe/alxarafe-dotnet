using Alxarafe.Modules.Ai.Application;
using Alxarafe.Modules.Ai.Persistence;
using Alxarafe.Modules.Ai.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class AiTenantUsageMigrationTests(TenantApplicationFactory factory) : IClassFixture<TenantApplicationFactory>
{
    [Fact]
    public async Task AttributionUpgradeAndRollbackPreserveHistoricalUsage()
    {
        using var http = factory.CreateClient();
        await using var original = await factory.Services.GetRequiredService<IDbContextFactory<AiUsageDbContext>>().CreateDbContextAsync();
        var connection = new NpgsqlConnectionStringBuilder(original.Database.GetConnectionString());
        string schema = "ai_tenant_upgrade_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(connection.ConnectionString);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin)) await create.ExecuteNonQueryAsync();
        connection.SearchPath = schema;
        var options = new DbContextOptionsBuilder<AiUsageDbContext>();
        AiUsageStorage.Configure(options, connection.ConnectionString, false);
        const string invocationId = "historical-invocation";
        try
        {
            await using var context = new AiUsageDbContext(options.Options);
            var migrator = context.GetService<IMigrator>();
            string previous = context.GetService<IMigrationsIdGenerator>().GetName(UsageMetering.MigrationId);
            await migrator.MigrateAsync(previous);
            await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO ai_usage_invocations (invocation_id, started_at, duration, outcome, model_calls, tool_calls, successful_tool_calls) VALUES ({invocationId}, {DateTimeOffset.UtcNow}, {0L}, {(int)AiInvocationOutcome.Completed}, {0}, {0}, {0})");
            await context.Database.MigrateAsync();
            Assert.False(context.Database.HasPendingModelChanges());
            var invocation = await context.Invocations.SingleAsync();
            Assert.Equal(invocationId, invocation.InvocationId);
            Assert.Null(invocation.TenantId);
            await migrator.MigrateAsync(previous);
            await context.Database.OpenConnectionAsync();
            await using var query = context.Database.GetDbConnection().CreateCommand();
            query.CommandText = "SELECT invocation_id FROM ai_usage_invocations";
            Assert.Equal(invocationId, await query.ExecuteScalarAsync());
            await context.Database.MigrateAsync();
            Assert.Null((await context.Invocations.AsNoTracking().SingleAsync()).TenantId);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
