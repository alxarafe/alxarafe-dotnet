using Alxarafe.Modules.Ai.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Alxarafe.Modules.Ai.Persistence.Migrations;

[DbContext(typeof(AiUsageDbContext))]
[Migration(MigrationId)]
public sealed class TenantUsageAttribution : Migration
{
    public const string MigrationId = "202610260002_TenantUsageAttribution";
    public const string TenantColumn = "tenant_id";
    private const string TenantIdCollation = "C";

    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>(TenantColumn, AiUsageStorage.InvocationsTable,
            maxLength: AiUsageStorage.MaximumIdentifierLength, nullable: true, collation: TenantIdCollation);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(TenantColumn, AiUsageStorage.InvocationsTable);

    protected override void BuildTargetModel(ModelBuilder modelBuilder) => ConfigureModel(modelBuilder);

    internal static void ConfigureModel(ModelBuilder modelBuilder)
    {
        UsageMetering.ConfigureModel(modelBuilder);
        modelBuilder.Entity<AiInvocationUsage>().Property(row => row.TenantId)
            .HasColumnName(TenantColumn).HasColumnType(AiUsageStorage.IdentifierColumnType)
            .HasMaxLength(AiUsageStorage.MaximumIdentifierLength).UseCollation(TenantIdCollation);
    }
}
