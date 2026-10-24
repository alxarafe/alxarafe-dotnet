using Alxarafe.Modules.Ai.Application;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Alxarafe.Modules.Ai.Persistence;

public static class AiUsageStorage
{
    public const string ConnectionName = "Platform";
    public const string ValidationDatabase = "alxarafe_test";
    public const string MigrationHistory = "__AiMigrationsHistory";
    public const string RecordsTable = "ai_usage_records";
    public const string InvocationsTable = "ai_usage_invocations";
    public const string RecordsPrimaryKey = "PK_ai_usage_records";
    public const string InvocationsPrimaryKey = "PK_ai_usage_invocations";
    public const string InvocationForeignKey = "FK_ai_usage_records_invocation";
    public const string AttemptIndex = "IX_ai_usage_records_invocation_call_attempt";
    public const string UserIndex = "IX_ai_usage_invocations_user_time";
    public const int MaximumIdentifierLength = AiUsageRecord.MaximumIdentifierLength;
    public const int ProviderCostPrecision = 28;
    public const int ProviderCostScale = 10;
    public static readonly string IdentifierColumnType = $"character varying({MaximumIdentifierLength})";
    public static readonly string ProviderCostColumnType = $"numeric({ProviderCostPrecision},{ProviderCostScale})";

    public static void Configure(DbContextOptionsBuilder options, string? connectionString, bool testing)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) throw new InvalidOperationException("The Platform connection string must be configured for AI usage.");
        if (testing && new NpgsqlConnectionStringBuilder(connectionString).Database != ValidationDatabase)
            throw new InvalidOperationException("Testing requires AI usage to use " + ValidationDatabase + ".");
        options.UseNpgsql(connectionString, postgres => postgres.MigrationsHistoryTable(MigrationHistory));
    }
}

public sealed class AiUsageDbContext(DbContextOptions<AiUsageDbContext> options) : DbContext(options)
{
    public DbSet<AiUsageRecord> UsageRecords => Set<AiUsageRecord>();
    public DbSet<AiInvocationUsage> Invocations => Set<AiInvocationUsage>();
    protected override void OnModelCreating(ModelBuilder modelBuilder) => Migrations.UsageMetering.ConfigureModel(modelBuilder);
}
