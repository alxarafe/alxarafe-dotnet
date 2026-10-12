using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Alxarafe.EntityFrameworkCore.Migrations;

[DbContext(typeof(PlatformDbContext))]
public sealed class PlatformDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
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
