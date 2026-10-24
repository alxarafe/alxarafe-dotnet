using Alxarafe.Modules.Wms.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Alxarafe.Modules.Wms.Infrastructure.Migrations;

[DbContext(typeof(WmsDbContext))]
[Migration(MigrationId)]
public sealed class InventoryFoundation : Migration
{
    public const string MigrationId = "202610240001_InventoryFoundation";
    internal const string MovementTable = "wms_inventory_movements";
    internal const string MovementColumns = "id, warehouse_id, item_id, location_id, occurred_at, quantity_delta, uom_id, type, external_key";
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE wms_warehouses (
                id uuid CONSTRAINT "PK_wms_warehouses" PRIMARY KEY,
                code text COLLATE "C" NOT NULL, name text NOT NULL);
            CREATE UNIQUE INDEX "IX_wms_warehouses_code" ON wms_warehouses(code);
            CREATE TABLE wms_locations (
                id uuid CONSTRAINT "PK_wms_locations" PRIMARY KEY,
                warehouse_id uuid NOT NULL, code text COLLATE "C" NOT NULL, enabled boolean NOT NULL,
                CONSTRAINT "AK_wms_locations_id_warehouse" UNIQUE(id, warehouse_id),
                CONSTRAINT "FK_wms_locations_warehouse" FOREIGN KEY (warehouse_id) REFERENCES wms_warehouses(id) ON DELETE RESTRICT);
            CREATE UNIQUE INDEX "IX_wms_locations_warehouse_code" ON wms_locations(warehouse_id, code);
            CREATE TABLE wms_items (
                id uuid CONSTRAINT "PK_wms_items" PRIMARY KEY,
                sku text COLLATE "C" NOT NULL, description text NOT NULL, base_uom_id uuid NOT NULL, item_family_id uuid,
                CONSTRAINT "AK_wms_items_id_uom" UNIQUE(id, base_uom_id),
                CONSTRAINT "FK_wms_items_uom" FOREIGN KEY (base_uom_id) REFERENCES wms_uoms(id) ON DELETE RESTRICT,
                CONSTRAINT "FK_wms_items_family" FOREIGN KEY (item_family_id) REFERENCES wms_item_families(id) ON DELETE RESTRICT);
            CREATE UNIQUE INDEX "IX_wms_items_sku" ON wms_items(sku);
            CREATE INDEX "IX_wms_items_uom" ON wms_items(base_uom_id);
            CREATE INDEX "IX_wms_items_family" ON wms_items(item_family_id);
            CREATE TABLE wms_inventory_movements (
                id uuid CONSTRAINT "PK_wms_inventory_movements" PRIMARY KEY,
                warehouse_id uuid NOT NULL, item_id uuid NOT NULL, location_id uuid NOT NULL,
                occurred_at timestamp with time zone NOT NULL, quantity_delta numeric(20,6) NOT NULL,
                uom_id uuid NOT NULL, type text COLLATE "C" NOT NULL, external_key text COLLATE "C" NOT NULL,
                CONSTRAINT "FK_wms_movements_location_warehouse" FOREIGN KEY (location_id, warehouse_id) REFERENCES wms_locations(id, warehouse_id) ON DELETE RESTRICT,
                CONSTRAINT "FK_wms_movements_item_uom" FOREIGN KEY (item_id, uom_id) REFERENCES wms_items(id, base_uom_id) ON DELETE RESTRICT);
            CREATE UNIQUE INDEX "IX_wms_movements_warehouse_key" ON wms_inventory_movements(warehouse_id, external_key);
            CREATE INDEX "IX_wms_movements_inventory_time" ON wms_inventory_movements(warehouse_id, item_id, location_id, occurred_at, id);
            CREATE INDEX "IX_wms_movements_location_warehouse" ON wms_inventory_movements(location_id, warehouse_id);
            CREATE INDEX "IX_wms_movements_item_uom" ON wms_inventory_movements(item_id, uom_id);
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(MovementTable);
        migrationBuilder.DropTable("wms_items");
        migrationBuilder.DropTable("wms_locations");
        migrationBuilder.DropTable("wms_warehouses");
    }
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => BuildFrozenModel(modelBuilder);
    internal static void BuildFrozenModel(ModelBuilder modelBuilder)
    {
        ItemFamilies.BuildFrozenModel(modelBuilder);
        ConfigureInventoryModel(modelBuilder);
    }
    // Frozen schema of this migration. Future changes belong to a new migration.
    internal static void ConfigureInventoryModel(ModelBuilder modelBuilder)
    {
        var warehouse = modelBuilder.Entity<Warehouse>();
        warehouse.ToTable("wms_warehouses");
        warehouse.HasKey(row => row.Id).HasName("PK_wms_warehouses");
        warehouse.Property(row => row.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        warehouse.Property(row => row.Code).HasColumnName("code").HasColumnType("text").UseCollation(WmsStorage.OrdinalCollation).IsRequired();
        warehouse.Property(row => row.Name).HasColumnName("name").HasColumnType("text").UseCollation(WmsStorage.OrdinalCollation).IsRequired();
        var location = modelBuilder.Entity<Location>();
        location.ToTable("wms_locations");
        location.HasKey(row => row.Id).HasName("PK_wms_locations");
        location.Property(row => row.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        location.Property(row => row.WarehouseId).HasColumnName("warehouse_id").HasColumnType("uuid");
        location.Property(row => row.Code).HasColumnName("code").HasColumnType("text").UseCollation(WmsStorage.OrdinalCollation).IsRequired();
        location.Property(row => row.Enabled).HasColumnName("enabled").HasColumnType("boolean");
        var stockItem = modelBuilder.Entity<StockItem>();
        stockItem.ToTable("wms_items");
        stockItem.HasKey(row => row.Id).HasName("PK_wms_items");
        stockItem.Property(row => row.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        stockItem.Property(row => row.Sku).HasColumnName("sku").HasColumnType("text").UseCollation(WmsStorage.OrdinalCollation).IsRequired();
        stockItem.Property(row => row.Description).HasColumnName("description").HasColumnType("text").UseCollation(WmsStorage.OrdinalCollation).IsRequired();
        stockItem.Property(row => row.BaseUomId).HasColumnName("base_uom_id").HasColumnType("uuid");
        stockItem.Property(row => row.ItemFamilyId).HasColumnName("item_family_id").HasColumnType("uuid");
        var inventoryMovement = modelBuilder.Entity<InventoryMovement>();
        inventoryMovement.ToTable("wms_inventory_movements");
        inventoryMovement.HasKey(row => row.Id).HasName("PK_wms_inventory_movements");
        inventoryMovement.Property(row => row.Id).HasColumnName("id").HasColumnType("uuid").ValueGeneratedNever();
        inventoryMovement.Property(row => row.WarehouseId).HasColumnName("warehouse_id").HasColumnType("uuid");
        inventoryMovement.Property(row => row.ItemId).HasColumnName("item_id").HasColumnType("uuid");
        inventoryMovement.Property(row => row.LocationId).HasColumnName("location_id").HasColumnType("uuid");
        inventoryMovement.Property(row => row.OccurredAt).HasColumnName("occurred_at").HasColumnType("timestamp with time zone");
        inventoryMovement.Property(row => row.QuantityDelta).HasColumnName("quantity_delta").HasColumnType("numeric(20,6)").HasPrecision(InventoryRules.QuantityPrecision, InventoryRules.QuantityScale);
        inventoryMovement.Property(row => row.UomId).HasColumnName("uom_id").HasColumnType("uuid");
        inventoryMovement.Property(row => row.Type).HasColumnName("type").HasColumnType("text").UseCollation(WmsStorage.OrdinalCollation).IsRequired();
        inventoryMovement.Property(row => row.ExternalKey).HasColumnName("external_key").HasColumnType("text").UseCollation(WmsStorage.OrdinalCollation).IsRequired();
        warehouse.HasIndex(row => row.Code).IsUnique().HasDatabaseName("IX_wms_warehouses_code");
        location.HasIndex(row => new { row.WarehouseId, row.Code }).IsUnique().HasDatabaseName("IX_wms_locations_warehouse_code");
        location.HasAlternateKey(row => new { row.Id, row.WarehouseId }).HasName("AK_wms_locations_id_warehouse");
        location.HasOne<Warehouse>().WithMany().HasForeignKey(row => row.WarehouseId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_wms_locations_warehouse");
        stockItem.HasIndex(row => row.Sku).IsUnique().HasDatabaseName("IX_wms_items_sku");
        stockItem.HasAlternateKey(row => new { row.Id, row.BaseUomId }).HasName("AK_wms_items_id_uom");
        stockItem.HasOne(typeof(UomRow).FullName!, null).WithMany().HasForeignKey("BaseUomId").OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_wms_items_uom");
        stockItem.HasOne(typeof(ItemFamilyRow).FullName!, null).WithMany().HasForeignKey("ItemFamilyId").OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_wms_items_family");
        stockItem.HasIndex(row => row.BaseUomId).HasDatabaseName("IX_wms_items_uom");
        stockItem.HasIndex(row => row.ItemFamilyId).HasDatabaseName("IX_wms_items_family");
        inventoryMovement.HasOne<Location>().WithMany().HasForeignKey(row => new { row.LocationId, row.WarehouseId })
            .HasPrincipalKey(row => new { row.Id, row.WarehouseId }).OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_wms_movements_location_warehouse");
        inventoryMovement.HasOne<StockItem>().WithMany().HasForeignKey(row => new { row.ItemId, row.UomId })
            .HasPrincipalKey(row => new { row.Id, row.BaseUomId }).OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_wms_movements_item_uom");
        inventoryMovement.HasIndex(row => new { row.WarehouseId, row.ExternalKey }).IsUnique().HasDatabaseName("IX_wms_movements_warehouse_key");
        inventoryMovement.HasIndex(row => new { row.WarehouseId, row.ItemId, row.LocationId, row.OccurredAt, row.Id }).HasDatabaseName("IX_wms_movements_inventory_time");
        inventoryMovement.HasIndex(row => new { row.LocationId, row.WarehouseId }).HasDatabaseName("IX_wms_movements_location_warehouse");
        inventoryMovement.HasIndex(row => new { row.ItemId, row.UomId }).HasDatabaseName("IX_wms_movements_item_uom");
    }
}
