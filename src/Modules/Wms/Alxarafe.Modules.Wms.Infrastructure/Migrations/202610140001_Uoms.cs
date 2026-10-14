using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Alxarafe.Modules.Wms.Infrastructure.Migrations;

[DbContext(typeof(WmsDbContext))]
[Migration("202610140001_Uoms")]
public sealed class Uoms : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: WmsStorage.Table,
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                code = table.Column<string>(type: "text", nullable: false, collation: WmsStorage.OrdinalCollation),
                description = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table => table.PrimaryKey(WmsStorage.PrimaryKey, row => row.id));
        migrationBuilder.CreateIndex(name: WmsStorage.CodeIndex, table: WmsStorage.Table,
            column: WmsStorage.CodeColumn, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(WmsStorage.Table);
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => BuildFrozenModel(modelBuilder);

    internal static void BuildFrozenModel(ModelBuilder modelBuilder)
    {
        // Frozen migration metadata, independent of future live-model changes.
        modelBuilder.HasAnnotation("ProductVersion", "10.0.0").HasAnnotation("Relational:MaxIdentifierLength", 63);
        modelBuilder.Entity("Alxarafe.Modules.Wms.Infrastructure.UomRow", uoms =>
        {
            uoms.Property<Guid>("Id").HasColumnType("uuid").HasColumnName("id");
            uoms.Property<string>("Code").IsRequired().HasColumnType("text").HasColumnName("code").UseCollation("C");
            uoms.Property<string>("Description").IsRequired().HasColumnType("text").HasColumnName("description");
            uoms.HasKey("Id").HasName("PK_wms_uoms");
            uoms.HasIndex("Code").IsUnique().HasDatabaseName("IX_wms_uoms_code");
            uoms.ToTable("wms_uoms");
        });
    }
}
