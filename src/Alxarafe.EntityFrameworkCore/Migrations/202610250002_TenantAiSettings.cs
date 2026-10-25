using Alxarafe.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Alxarafe.EntityFrameworkCore.Migrations;

[DbContext(typeof(PlatformDbContext))]
[Migration(MigrationId)]
public sealed class TenantAiSettingsMigration : Migration
{
    public const string MigrationId = "202610250002_TenantAiSettings";

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.CreateTable(
        name: TenantAiSettingsStorage.Table,
        columns: table => new
        {
            tenant_id = table.Column<string>(maxLength: TenantAiSettings.MaximumTenantIdLength,
                nullable: false, collation: PlatformStorage.IdCollation),
            provider_override = table.Column<string>(maxLength: TenantAiSettings.MaximumSelectionLength, nullable: true),
            model_override = table.Column<string>(maxLength: TenantAiSettings.MaximumSelectionLength, nullable: true)
        },
        constraints: table =>
        {
            table.PrimaryKey(TenantAiSettingsStorage.PrimaryKey, row => row.tenant_id);
            table.CheckConstraint(TenantAiSettingsStorage.TenantIdConstraint, TenantAiSettingsStorage.TenantIdCheck);
        });

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(TenantAiSettingsStorage.Table);
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => ConfigureFrozenModel(modelBuilder);

    internal static void ConfigureFrozenModel(ModelBuilder modelBuilder)
    {
        // Frozen migration metadata must not follow future changes to the live model.
        modelBuilder.HasAnnotation("ProductVersion", "10.0.0")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);
        modelBuilder.Entity("Alxarafe.EntityFrameworkCore.ModuleStateRow", states =>
        {
            states.Property<string>("ModuleId").IsRequired().HasColumnType("text")
                .HasColumnName("module_id").UseCollation("C");
            states.Property<bool>("Enabled").HasColumnType("boolean").HasColumnName("enabled");
            states.HasKey("ModuleId").HasName("PK_module_states");
            states.ToTable("module_states");
        });
        modelBuilder.Entity("Alxarafe.EntityFrameworkCore.TenantAiSettingsRow", settings =>
        {
            settings.Property<string>("TenantId").IsRequired().HasMaxLength(256)
                .HasColumnType("character varying(256)").HasColumnName("tenant_id").UseCollation("C");
            settings.Property<string>("ProviderOverride").HasMaxLength(256)
                .HasColumnType("character varying(256)").HasColumnName("provider_override");
            settings.Property<string>("ModelOverride").HasMaxLength(256)
                .HasColumnType("character varying(256)").HasColumnName("model_override");
            settings.HasKey("TenantId").HasName("PK_tenant_ai_settings");
            settings.ToTable("tenant_ai_settings", table => table.HasCheckConstraint("CK_tenant_ai_settings_tenant_id", "btrim(tenant_id) <> ''"));
        });
    }
}
