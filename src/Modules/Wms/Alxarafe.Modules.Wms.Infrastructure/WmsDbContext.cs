using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Alxarafe.Modules.Wms.Infrastructure;

public static class WmsStorage
{
    public const string ConnectionName = "Wms";
    public const string TestingEnvironment = "Testing";
    public const string ValidationDatabase = "alxarafe_test";
    public const string MigrationHistory = "__WmsMigrationsHistory";
    public const string UomTable = "wms_uoms";
    public const string StorageAttributeTable = "wms_storage_attributes";
    public const string StorageAttributePrimaryKey = "PK_wms_storage_attributes";
    public const string StorageAttributeCodeIndex = "IX_wms_storage_attributes_code";
    public const string ItemFamilyTable = "wms_item_families";
    public const string ItemFamilyPrimaryKey = "PK_wms_item_families";
    public const string ItemFamilyCodeIndex = "IX_wms_item_families_code";
    public const string AssociationTable = "wms_item_family_storage_attributes";
    public const string AssociationPrimaryKey = "PK_wms_item_family_storage_attributes";
    public const string FamilyIdColumn = "item_family_id";
    public const string AttributeIdColumn = "storage_attribute_id";
    public const string AssociationAttributeIndex = "IX_wms_item_family_storage_attributes_storage_attribute_id";
    public const string FamilyForeignKey = "FK_wms_association_family";
    public const string AttributeForeignKey = "FK_wms_association_attribute";
    public const string NameColumn = "name";
    // Compatibility aliases used only by the unchanged historical Uoms migration.
    public const string Table = UomTable;
    public const string IdColumn = "id";
    public const string CodeColumn = "code";
    public const string DescriptionColumn = "description";
    public const string UomCodeIndex = "IX_wms_uoms_code";
    public const string CodeIndex = UomCodeIndex;
    public const string UomPrimaryKey = "PK_wms_uoms";
    public const string PrimaryKey = UomPrimaryKey;
    public const string OrdinalCollation = "C";

    public static void Configure(DbContextOptionsBuilder options, string? connectionString, bool testing)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("The Wms connection string must be configured.");
        if (testing && !string.Equals(new NpgsqlConnectionStringBuilder(connectionString).Database, ValidationDatabase, StringComparison.Ordinal))
            throw new InvalidOperationException("Testing requires the Wms connection to use " + ValidationDatabase + ".");
        options.UseNpgsql(connectionString, postgres => postgres.MigrationsHistoryTable(MigrationHistory));
    }
}

public sealed class UomRow
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public sealed class StorageAttributeRow
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class ItemFamilyRow
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class ItemFamilyAssociationRow
{
    public Guid ItemFamilyId { get; set; }
    public Guid StorageAttributeId { get; set; }
}

public sealed class WmsDbContext(DbContextOptions<WmsDbContext> options) : DbContext(options)
{
    public DbSet<UomRow> Uoms => Set<UomRow>();
    public DbSet<StorageAttributeRow> StorageAttributes => Set<StorageAttributeRow>();

    public DbSet<ItemFamilyRow> ItemFamilies => Set<ItemFamilyRow>();
    public DbSet<ItemFamilyAssociationRow> ItemFamilyAssociations => Set<ItemFamilyAssociationRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        Migrations.InventoryFoundation.ConfigureInventoryModel(modelBuilder);
        var uoms = modelBuilder.Entity<UomRow>();
        uoms.ToTable(WmsStorage.UomTable);
        uoms.HasKey(uom => uom.Id).HasName(WmsStorage.UomPrimaryKey);
        uoms.Property(uom => uom.Id).HasColumnName(WmsStorage.IdColumn).HasColumnType("uuid").ValueGeneratedNever();
        uoms.Property(uom => uom.Code).HasColumnName(WmsStorage.CodeColumn).HasColumnType("text")
            .UseCollation(WmsStorage.OrdinalCollation).IsRequired();
        uoms.Property(uom => uom.Description).HasColumnName(WmsStorage.DescriptionColumn).HasColumnType("text").IsRequired();
        uoms.HasIndex(uom => uom.Code).IsUnique().HasDatabaseName(WmsStorage.UomCodeIndex);

        var families = modelBuilder.Entity<ItemFamilyRow>();
        families.ToTable(WmsStorage.ItemFamilyTable);
        families.HasKey(family => family.Id).HasName(WmsStorage.ItemFamilyPrimaryKey);
        families.Property(family => family.Id).HasColumnName(WmsStorage.IdColumn).HasColumnType("uuid").ValueGeneratedNever();
        families.Property(family => family.Code).HasColumnName(WmsStorage.CodeColumn).HasColumnType("text")
            .UseCollation(WmsStorage.OrdinalCollation).IsRequired();
        families.Property(family => family.Name).HasColumnName(WmsStorage.NameColumn).HasColumnType("text").IsRequired();
        families.HasIndex(family => family.Code).IsUnique().HasDatabaseName(WmsStorage.ItemFamilyCodeIndex);

        var associations = modelBuilder.Entity<ItemFamilyAssociationRow>();
        associations.ToTable(WmsStorage.AssociationTable);
        associations.HasKey(pair => new { pair.ItemFamilyId, pair.StorageAttributeId }).HasName(WmsStorage.AssociationPrimaryKey);
        associations.Property(pair => pair.ItemFamilyId).HasColumnName(WmsStorage.FamilyIdColumn).HasColumnType("uuid");
        associations.Property(pair => pair.StorageAttributeId).HasColumnName(WmsStorage.AttributeIdColumn).HasColumnType("uuid");
        associations.HasOne<ItemFamilyRow>().WithMany().HasForeignKey(pair => pair.ItemFamilyId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName(WmsStorage.FamilyForeignKey);
        associations.HasOne<StorageAttributeRow>().WithMany().HasForeignKey(pair => pair.StorageAttributeId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName(WmsStorage.AttributeForeignKey);
        associations.HasIndex(pair => pair.StorageAttributeId).HasDatabaseName(WmsStorage.AssociationAttributeIndex);

        var attributes = modelBuilder.Entity<StorageAttributeRow>();
        attributes.ToTable(WmsStorage.StorageAttributeTable);
        attributes.HasKey(attribute => attribute.Id).HasName(WmsStorage.StorageAttributePrimaryKey);
        attributes.Property(attribute => attribute.Id).HasColumnName(WmsStorage.IdColumn).HasColumnType("uuid").ValueGeneratedNever();
        attributes.Property(attribute => attribute.Code).HasColumnName(WmsStorage.CodeColumn).HasColumnType("text")
            .UseCollation(WmsStorage.OrdinalCollation).IsRequired();
        attributes.Property(attribute => attribute.Name).HasColumnName(WmsStorage.NameColumn).HasColumnType("text").IsRequired();
        attributes.HasIndex(attribute => attribute.Code).IsUnique().HasDatabaseName(WmsStorage.StorageAttributeCodeIndex);
    }
}
