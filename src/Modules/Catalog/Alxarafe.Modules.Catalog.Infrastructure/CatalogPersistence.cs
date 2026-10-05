using Alxarafe.Modules.Catalog.Application;
using Alxarafe.Modules.Catalog.Domain;
using Microsoft.EntityFrameworkCore;

namespace Alxarafe.Modules.Catalog.Infrastructure;

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<CatalogItemRow> Items => Set<CatalogItemRow>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CatalogItemRow>(entity =>
        {
            entity.ToTable("catalog_items");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Sku).HasMaxLength(50).IsRequired();
            entity.HasIndex(x => x.Sku).IsUnique();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
        });
    }
}

public sealed class CatalogItemRow
{
    public Guid Id { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class EfItemRepository(CatalogDbContext db) : IItemRepository
{
    public async Task AddAsync(Item item, CancellationToken cancellationToken = default)
    {
        db.Items.Add(new CatalogItemRow { Id = item.Id.Value, Sku = item.Sku.Value, Name = item.Name.Value });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<Item?> GetByIdAsync(ItemId id, CancellationToken cancellationToken = default)
    {
        var row = await db.Items.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id.Value, cancellationToken);
        return row is null ? null : Item.Rehydrate(new ItemId(row.Id), Sku.Create(row.Sku), ItemName.Create(row.Name));
    }

    public Task<bool> ExistsBySkuAsync(Sku sku, CancellationToken cancellationToken = default) =>
        db.Items.AsNoTracking().AnyAsync(x => x.Sku == sku.Value, cancellationToken);
}
