using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Alxarafe.EntityFrameworkCore.Migrations;

[DbContext(typeof(PlatformDbContext))]
[Migration("202610120001_ModuleStates")]
public sealed class ModuleStates : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.CreateTable(
        name: PlatformStorage.ModuleStatesTable,
        columns: table => new
        {
            module_id = table.Column<string>(type: "text", nullable: false, collation: PlatformStorage.IdCollation),
            enabled = table.Column<bool>(type: "boolean", nullable: false)
        },
        constraints: table => table.PrimaryKey(PlatformStorage.ModuleStatesPrimaryKey, row => row.module_id));

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(PlatformStorage.ModuleStatesTable);

    protected override void BuildTargetModel(ModelBuilder modelBuilder)
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
    }
}
