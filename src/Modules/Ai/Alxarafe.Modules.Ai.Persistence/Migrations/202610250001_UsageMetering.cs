using System.Text.Json;
using Alxarafe.Modules.Ai.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Alxarafe.Modules.Ai.Persistence.Migrations;

[DbContext(typeof(AiUsageDbContext))]
[Migration(MigrationId)]
public sealed class UsageMetering : Migration
{
    public const string MigrationId = "202610250001_UsageMetering";
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(AiUsageStorage.InvocationsTable, columns: table => new
        {
            invocation_id = table.Column<string>(maxLength: AiUsageStorage.MaximumIdentifierLength, nullable: false),
            user_id = table.Column<Guid>(type: "uuid", nullable: true),
            started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            duration = table.Column<long>(type: "bigint", nullable: false),
            outcome = table.Column<int>(type: "integer", nullable: false),
            model_calls = table.Column<int>(type: "integer", nullable: false),
            tool_calls = table.Column<int>(type: "integer", nullable: false),
            successful_tool_calls = table.Column<int>(type: "integer", nullable: false)
        }, constraints: table => table.PrimaryKey(AiUsageStorage.InvocationsPrimaryKey, row => row.invocation_id));
        migrationBuilder.CreateTable(AiUsageStorage.RecordsTable, columns: table => new
        {
            id = table.Column<Guid>(type: "uuid", nullable: false),
            invocation_id = table.Column<string>(maxLength: AiUsageStorage.MaximumIdentifierLength, nullable: false),
            model_call_id = table.Column<Guid>(type: "uuid", nullable: false),
            attempt = table.Column<int>(type: "integer", nullable: false),
            provider = table.Column<string>(maxLength: AiUsageStorage.MaximumIdentifierLength, nullable: true),
            model = table.Column<string>(maxLength: AiUsageStorage.MaximumIdentifierLength, nullable: false),
            reported_model = table.Column<string>(maxLength: AiUsageStorage.MaximumIdentifierLength, nullable: true),
            started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            duration = table.Column<long>(type: "bigint", nullable: false),
            outcome = table.Column<int>(type: "integer", nullable: false),
            input_tokens = table.Column<long>(type: "bigint", nullable: true),
            output_tokens = table.Column<long>(type: "bigint", nullable: true),
            reasoning_tokens = table.Column<long>(type: "bigint", nullable: true),
            cached_tokens = table.Column<long>(type: "bigint", nullable: true),
            provider_request_id = table.Column<string>(maxLength: AiUsageStorage.MaximumIdentifierLength, nullable: true),
            provider_response_id = table.Column<string>(maxLength: AiUsageStorage.MaximumIdentifierLength, nullable: true),
            provider_cost = table.Column<decimal>(precision: AiUsageStorage.ProviderCostPrecision, scale: AiUsageStorage.ProviderCostScale, nullable: true),
            provider_cost_currency = table.Column<string>(maxLength: AiUsageStorage.MaximumIdentifierLength, nullable: true),
            billing_mode = table.Column<int>(type: "integer", nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey(AiUsageStorage.RecordsPrimaryKey, row => row.id);
            table.ForeignKey(AiUsageStorage.InvocationForeignKey, row => row.invocation_id,
                AiUsageStorage.InvocationsTable, "invocation_id", onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex(AiUsageStorage.AttemptIndex, AiUsageStorage.RecordsTable, ["invocation_id", "model_call_id", "attempt"], unique: true);
        migrationBuilder.CreateIndex(AiUsageStorage.UserIndex, AiUsageStorage.InvocationsTable, ["user_id", "started_at"]);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(AiUsageStorage.RecordsTable);
        migrationBuilder.DropTable(AiUsageStorage.InvocationsTable);
    }
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => ConfigureModel(modelBuilder);

    // Frozen schema. Future properties require a new migration and snapshot.
    internal static void ConfigureModel(ModelBuilder modelBuilder)
    {
        var invocation = modelBuilder.Entity<AiInvocationUsage>();
        invocation.Ignore(row => row.TenantId);
        invocation.ToTable(AiUsageStorage.InvocationsTable);
        invocation.Property<string>("InvocationId").HasColumnType(AiUsageStorage.IdentifierColumnType).IsRequired();
        invocation.Property<Guid?>("UserId").HasColumnType("uuid").IsRequired(false);
        invocation.Property<DateTimeOffset>("StartedAt").HasColumnType("timestamp with time zone").IsRequired();
        invocation.Property<AiInvocationOutcome>("Outcome").HasColumnType("integer").IsRequired();
        invocation.Property<int>("ModelCalls").HasColumnType("integer").IsRequired();
        invocation.Property<int>("ToolCalls").HasColumnType("integer").IsRequired();
        invocation.Property<int>("SuccessfulToolCalls").HasColumnType("integer").IsRequired();
        invocation.HasKey(row => row.InvocationId).HasName(AiUsageStorage.InvocationsPrimaryKey);
        invocation.Property(row => row.InvocationId).ValueGeneratedNever();
        invocation.Property(row => row.Duration).HasConversion<long>().HasColumnType("bigint");
        invocation.HasIndex(row => new { row.UserId, row.StartedAt }).HasDatabaseName(AiUsageStorage.UserIndex);
        var usage = modelBuilder.Entity<AiUsageRecord>();
        usage.ToTable(AiUsageStorage.RecordsTable);
        usage.Property<Guid>("Id").HasColumnType("uuid").IsRequired();
        usage.Property<string>("InvocationId").HasColumnType(AiUsageStorage.IdentifierColumnType).IsRequired();
        usage.Property<Guid>("ModelCallId").HasColumnType("uuid").IsRequired();
        usage.Property<int>("Attempt").HasColumnType("integer").IsRequired();
        usage.Property<string>("Provider").HasColumnType(AiUsageStorage.IdentifierColumnType).IsRequired(false);
        usage.Property<string>("Model").HasColumnType(AiUsageStorage.IdentifierColumnType).IsRequired();
        usage.Property<string>("ReportedModel").HasColumnType(AiUsageStorage.IdentifierColumnType).IsRequired(false);
        usage.Property<DateTimeOffset>("StartedAt").HasColumnType("timestamp with time zone").IsRequired();
        usage.Property<AiExecutionOutcome>("Outcome").HasColumnType("integer").IsRequired();
        usage.Property<long?>("InputTokens").HasColumnType("bigint").IsRequired(false);
        usage.Property<long?>("OutputTokens").HasColumnType("bigint").IsRequired(false);
        usage.Property<long?>("ReasoningTokens").HasColumnType("bigint").IsRequired(false);
        usage.Property<long?>("CachedTokens").HasColumnType("bigint").IsRequired(false);
        usage.Property<string>("ProviderRequestId").HasColumnType(AiUsageStorage.IdentifierColumnType).IsRequired(false);
        usage.Property<string>("ProviderResponseId").HasColumnType(AiUsageStorage.IdentifierColumnType).IsRequired(false);
        usage.Property<string>("ProviderCostCurrency").HasColumnType(AiUsageStorage.IdentifierColumnType).IsRequired(false);
        usage.Property<decimal?>("ProviderCost").HasColumnType(AiUsageStorage.ProviderCostColumnType).IsRequired(false);
        usage.Property<AiBillingMode?>("BillingMode").HasColumnType("integer").IsRequired(false);
        usage.HasKey(row => row.Id).HasName(AiUsageStorage.RecordsPrimaryKey);
        usage.Property(row => row.Id).ValueGeneratedNever();
        usage.Property(row => row.InvocationId).IsRequired();
        usage.Property(row => row.Duration).HasConversion<long>().HasColumnType("bigint");
        usage.Property(row => row.ProviderCost).HasPrecision(AiUsageStorage.ProviderCostPrecision, AiUsageStorage.ProviderCostScale);
        usage.HasIndex(row => new { row.InvocationId, row.ModelCallId, row.Attempt }).IsUnique().HasDatabaseName(AiUsageStorage.AttemptIndex);
        usage.HasOne<AiInvocationUsage>().WithMany().HasForeignKey(row => row.InvocationId).OnDelete(DeleteBehavior.Restrict).HasConstraintName(AiUsageStorage.InvocationForeignKey);
        foreach (var entity in new[] { invocation.Metadata, usage.Metadata })
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(JsonNamingPolicy.SnakeCaseLower.ConvertName(property.Name));
                if (property.ClrType == typeof(string)) property.SetMaxLength(AiUsageStorage.MaximumIdentifierLength);
            }
    }
}
