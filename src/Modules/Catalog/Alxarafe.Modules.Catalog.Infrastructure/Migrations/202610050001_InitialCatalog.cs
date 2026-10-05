using Alxarafe.Modules.Catalog.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alxarafe.Modules.Catalog.Infrastructure.Migrations;

[DbContext(typeof(CatalogDbContext))]
[Migration("202610050001_InitialCatalog")]
public partial class InitialCatalog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "catalog_items",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Sku = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_catalog_items", x => x.Id));

        migrationBuilder.CreateIndex("IX_catalog_items_Sku", "catalog_items", "Sku", unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("catalog_items");
}
