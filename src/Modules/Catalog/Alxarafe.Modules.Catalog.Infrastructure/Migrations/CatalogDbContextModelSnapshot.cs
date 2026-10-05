using Alxarafe.Modules.Catalog.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace Alxarafe.Modules.Catalog.Infrastructure.Migrations;

[DbContext(typeof(CatalogDbContext))]
sealed partial class CatalogDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasAnnotation("ProductVersion", "10.0.0")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);
        modelBuilder.Entity("Alxarafe.Modules.Catalog.Infrastructure.CatalogItemRow", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedNever().HasColumnType("uuid");
            b.Property<string>("Name").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)");
            b.Property<string>("Sku").IsRequired().HasMaxLength(50).HasColumnType("character varying(50)");
            b.HasKey("Id");
            b.HasIndex("Sku").IsUnique();
            b.ToTable("catalog_items");
        });
    }
}
