using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Alxarafe.Modules.Wms.Infrastructure.Migrations;

[DbContext(typeof(WmsDbContext))]
[Migration("202610150001_StorageAttributes")]
public sealed class StorageAttributes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: WmsStorage.StorageAttributeTable,
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                code = table.Column<string>(type: "text", nullable: false, collation: WmsStorage.OrdinalCollation),
                name = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table => table.PrimaryKey(WmsStorage.StorageAttributePrimaryKey, row => row.id));
        migrationBuilder.CreateIndex(name: WmsStorage.StorageAttributeCodeIndex, table: WmsStorage.StorageAttributeTable,
            column: WmsStorage.CodeColumn, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(WmsStorage.StorageAttributeTable);
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => BuildFrozenModel(modelBuilder);

    internal static void BuildFrozenModel(ModelBuilder modelBuilder)
    {
        Uoms.BuildFrozenModel(modelBuilder);
        modelBuilder.Entity("Alxarafe.Modules.Wms.Infrastructure.StorageAttributeRow", attributes =>
        {
            attributes.Property<Guid>("Id").HasColumnType("uuid").HasColumnName("id");
            attributes.Property<string>("Code").IsRequired().HasColumnType("text").HasColumnName("code").UseCollation("C");
            attributes.Property<string>("Name").IsRequired().HasColumnType("text").HasColumnName("name");
            attributes.HasKey("Id").HasName("PK_wms_storage_attributes");
            attributes.HasIndex("Code").IsUnique().HasDatabaseName("IX_wms_storage_attributes_code");
            attributes.ToTable("wms_storage_attributes");
        });
    }
}
