using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Alxarafe.EntityFrameworkCore.Migrations;

[DbContext(typeof(PlatformDbContext))]
[Migration(MigrationId)]
public sealed class AuthoritativeTenancy : Migration
{
    public const string MigrationId = "202610260001_AuthoritativeTenancy";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("tenants", columns: table => new
        {
            id = table.Column<string>(maxLength: 256, nullable: false, collation: "C"),
            key = table.Column<string>(maxLength: 128, nullable: false, collation: "C"),
            display_name = table.Column<string>(maxLength: 256, nullable: false),
            active = table.Column<bool>(nullable: false),
            database_reference = table.Column<string>(maxLength: 128, nullable: false, collation: "C")
        }, constraints: table =>
        {
            table.PrimaryKey("PK_tenants", row => row.id);
            table.CheckConstraint("CK_tenants_metadata", "btrim(id) <> '' AND btrim(key) <> '' AND btrim(display_name) <> '' AND database_reference ~ '^[A-Za-z0-9_-]+$'");
        });
        migrationBuilder.CreateIndex("IX_tenants_Key", "tenants", "key", unique: true);
        migrationBuilder.CreateIndex("IX_tenants_DatabaseReference", "tenants", "database_reference", unique: true);
        migrationBuilder.CreateTable("tenant_memberships", columns: table => new
        {
            user_id = table.Column<Guid>(nullable: false),
            tenant_id = table.Column<string>(maxLength: 256, nullable: false, collation: "C"),
            active = table.Column<bool>(nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_tenant_memberships", row => new { row.user_id, row.tenant_id });
            table.ForeignKey("FK_tenant_memberships_tenants", row => row.tenant_id, "tenants", "id", onDelete: ReferentialAction.Cascade);
        });
        migrationBuilder.CreateIndex("IX_tenant_memberships_TenantId", "tenant_memberships", "tenant_id");
        // Preserve legacy unbound rows unchanged; only explicit canonical links are usable.
        migrationBuilder.AddColumn<string>("canonical_tenant_id", "tenant_ai_settings", maxLength: 256, nullable: true, collation: "C");
        migrationBuilder.CreateIndex("IX_tenant_ai_settings_canonical_tenant_id", "tenant_ai_settings", "canonical_tenant_id", unique: true);
        migrationBuilder.AddForeignKey("FK_tenant_ai_settings_tenants", "tenant_ai_settings", "canonical_tenant_id", "tenants", principalColumn: "id", onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey("FK_tenant_ai_settings_tenants", "tenant_ai_settings");
        migrationBuilder.DropIndex("IX_tenant_ai_settings_canonical_tenant_id", "tenant_ai_settings");
        migrationBuilder.DropColumn("canonical_tenant_id", "tenant_ai_settings");
        migrationBuilder.DropTable("tenant_memberships");
        migrationBuilder.DropTable("tenants");
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder) => ConfigureFrozenModel(modelBuilder);
    internal static void ConfigureFrozenModel(ModelBuilder modelBuilder)
    {
        TenantAiSettingsMigration.ConfigureFrozenModel(modelBuilder);
        modelBuilder.Entity("Alxarafe.EntityFrameworkCore.TenantRow", tenants =>
        {
            tenants.Property<string>("Id").IsRequired().HasMaxLength(256).HasColumnType("character varying(256)").HasColumnName("id").UseCollation("C");
            tenants.Property<string>("Key").IsRequired().HasMaxLength(128).HasColumnType("character varying(128)").HasColumnName("key").UseCollation("C");
            tenants.Property<string>("DisplayName").IsRequired().HasMaxLength(256).HasColumnType("character varying(256)").HasColumnName("display_name");
            tenants.Property<bool>("Active").HasColumnType("boolean").HasColumnName("active");
            tenants.Property<string>("DatabaseReference").IsRequired().HasMaxLength(128).HasColumnType("character varying(128)").HasColumnName("database_reference").UseCollation("C");
            tenants.HasKey("Id");
            tenants.HasIndex("Key").IsUnique();
            tenants.HasIndex("DatabaseReference").IsUnique();
            tenants.ToTable("tenants", table => table.HasCheckConstraint("CK_tenants_metadata", "btrim(id) <> '' AND btrim(key) <> '' AND btrim(display_name) <> '' AND database_reference ~ '^[A-Za-z0-9_-]+$'"));
        });
        modelBuilder.Entity("Alxarafe.EntityFrameworkCore.TenantMembershipRow", memberships =>
        {
            memberships.Property<Guid>("UserId").HasColumnType("uuid").HasColumnName("user_id");
            memberships.Property<string>("TenantId").HasMaxLength(256).HasColumnType("character varying(256)").HasColumnName("tenant_id").UseCollation("C");
            memberships.Property<bool>("Active").HasColumnType("boolean").HasColumnName("active");
            memberships.HasKey("UserId", "TenantId");
            memberships.HasIndex("TenantId");
            memberships.ToTable("tenant_memberships");
            memberships.HasOne("Alxarafe.EntityFrameworkCore.TenantRow").WithMany().HasForeignKey("TenantId").OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_tenant_memberships_tenants");
        });
        modelBuilder.Entity("Alxarafe.EntityFrameworkCore.TenantAiSettingsRow", settings =>
        {
            settings.Property<string>("CanonicalTenantId").HasMaxLength(256).HasColumnType("character varying(256)").HasColumnName("canonical_tenant_id").UseCollation("C");
            settings.HasIndex("CanonicalTenantId").IsUnique().HasDatabaseName("IX_tenant_ai_settings_canonical_tenant_id");
            settings.HasOne("Alxarafe.EntityFrameworkCore.TenantRow").WithMany().HasForeignKey("CanonicalTenantId").OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_tenant_ai_settings_tenants");
        });
    }
}
