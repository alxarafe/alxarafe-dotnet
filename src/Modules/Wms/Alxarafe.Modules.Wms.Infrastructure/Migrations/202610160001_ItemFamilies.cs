using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Alxarafe.Modules.Wms.Infrastructure.Migrations;

[DbContext(typeof(WmsDbContext))]
[Migration("202610160001_ItemFamilies")]
public sealed class ItemFamilies : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: WmsStorage.ItemFamilyTable,
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                code = table.Column<string>(type: "text", nullable: false, collation: WmsStorage.OrdinalCollation),
                name = table.Column<string>(type: "text", nullable: false)
            }, constraints: table => table.PrimaryKey(WmsStorage.ItemFamilyPrimaryKey, row => row.id));
        migrationBuilder.CreateIndex(name: WmsStorage.ItemFamilyCodeIndex, table: WmsStorage.ItemFamilyTable,
            column: WmsStorage.CodeColumn, unique: true);
        migrationBuilder.CreateTable(name: WmsStorage.AssociationTable,
            columns: table => new
            {
                item_family_id = table.Column<Guid>(type: "uuid", nullable: false),
                storage_attribute_id = table.Column<Guid>(type: "uuid", nullable: false)
            }, constraints: table =>
            {
                table.PrimaryKey(WmsStorage.AssociationPrimaryKey, row => new { row.item_family_id, row.storage_attribute_id });
                table.ForeignKey(WmsStorage.FamilyForeignKey, row => row.item_family_id, WmsStorage.ItemFamilyTable,
                    WmsStorage.IdColumn, onDelete: ReferentialAction.Restrict);
                table.ForeignKey(WmsStorage.AttributeForeignKey, row => row.storage_attribute_id, WmsStorage.StorageAttributeTable,
                    WmsStorage.IdColumn, onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex(name: WmsStorage.AssociationAttributeIndex, table: WmsStorage.AssociationTable, column: WmsStorage.AttributeIdColumn);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(WmsStorage.AssociationTable);
        migrationBuilder.DropTable(WmsStorage.ItemFamilyTable);
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder) => BuildFrozenModel(modelBuilder);

    internal static void BuildFrozenModel(ModelBuilder modelBuilder)
    {
        StorageAttributes.BuildFrozenModel(modelBuilder);
        modelBuilder.Entity("Alxarafe.Modules.Wms.Infrastructure.ItemFamilyRow", families =>
        {
            families.Property<Guid>("Id").HasColumnType("uuid").HasColumnName(WmsStorage.IdColumn);
            families.Property<string>("Code").IsRequired().HasColumnType("text").HasColumnName(WmsStorage.CodeColumn).UseCollation(WmsStorage.OrdinalCollation);
            families.Property<string>("Name").IsRequired().HasColumnType("text").HasColumnName(WmsStorage.NameColumn);
            families.HasKey("Id").HasName(WmsStorage.ItemFamilyPrimaryKey);
            families.HasIndex("Code").IsUnique().HasDatabaseName(WmsStorage.ItemFamilyCodeIndex);
            families.ToTable(WmsStorage.ItemFamilyTable);
        });
        modelBuilder.Entity("Alxarafe.Modules.Wms.Infrastructure.ItemFamilyAssociationRow", pairs =>
        {
            pairs.Property<Guid>("ItemFamilyId").HasColumnType("uuid").HasColumnName(WmsStorage.FamilyIdColumn);
            pairs.Property<Guid>("StorageAttributeId").HasColumnType("uuid").HasColumnName(WmsStorage.AttributeIdColumn);
            pairs.HasKey("ItemFamilyId", "StorageAttributeId").HasName(WmsStorage.AssociationPrimaryKey);
            pairs.HasIndex("StorageAttributeId").HasDatabaseName(WmsStorage.AssociationAttributeIndex);
            pairs.ToTable(WmsStorage.AssociationTable);
            pairs.HasOne("Alxarafe.Modules.Wms.Infrastructure.ItemFamilyRow", null).WithMany().HasForeignKey("ItemFamilyId")
                .OnDelete(DeleteBehavior.Restrict).IsRequired().HasConstraintName(WmsStorage.FamilyForeignKey);
            pairs.HasOne("Alxarafe.Modules.Wms.Infrastructure.StorageAttributeRow", null).WithMany().HasForeignKey("StorageAttributeId")
                .OnDelete(DeleteBehavior.Restrict).IsRequired().HasConstraintName(WmsStorage.AttributeForeignKey);
        });
    }
}
