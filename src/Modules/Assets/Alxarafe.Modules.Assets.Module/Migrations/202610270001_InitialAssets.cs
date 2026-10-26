using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Alxarafe.Modules.Assets.Migrations;

[DbContext(typeof(AssetsDbContext))]
[Migration("202610270001_InitialAssets")]
public sealed class InitialAssets : Migration
{
    internal static void ConfigureModel(ModelBuilder model)
    {
        model.Ignore<AssetCatalogueRow>();
        Catalogue<AssetTypeRow>(model, AssetsStorage.Types);
        Catalogue<ManufacturerRow>(model, AssetsStorage.Manufacturers);
        Catalogue<AssetModelRow>(model, AssetsStorage.Models);
        Catalogue<AssetLocationRow>(model, AssetsStorage.Locations);
        Catalogue<AssetStatusRow>(model, AssetsStorage.Statuses);
        model.Entity<AssetTypeRow>().Property(row => row.Description).HasColumnName(AssetFields.Description);
        model.Entity<AssetModelRow>().Property(row => row.ManufacturerId).HasColumnName(AssetFields.ManufacturerId);
        model.Entity<AssetModelRow>().HasOne<ManufacturerRow>().WithMany().HasForeignKey(row => row.ManufacturerId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<AssetModelRow>().HasIndex(row => new { row.ManufacturerId, row.Code }).IsUnique();
        var assets = model.Entity<AssetRow>();
        assets.ToTable(AssetsStorage.Assets, table => table.HasCheckConstraint(AssetsStorage.ConditionConstraint, $"\"{AssetFields.Condition}\" BETWEEN 0 AND {AssetLimits.MaximumCondition}"));
        assets.HasKey(row => row.Id);
        assets.Property(row => row.Id).ValueGeneratedNever();
        assets.Property(row => row.Name).IsRequired().UseCollation(AssetsStorage.OrdinalCollation);
        assets.Property(row => row.AssetTag).UseCollation(AssetsStorage.OrdinalCollation);
        assets.Property(row => row.Lifecycle).IsRequired().UseCollation(AssetsStorage.OrdinalCollation);
        assets.Property(row => row.PurchasePrice).HasPrecision(AssetLimits.PricePrecision, AssetLimits.PriceScale);
        assets.HasIndex(row => row.AssetTag).IsUnique();
        assets.HasIndex(row => row.AssetTypeId);
        assets.HasIndex(row => row.ManufacturerId);
        assets.HasIndex(row => row.AssetModelId);
        assets.HasIndex(row => row.LocationId);
        assets.HasIndex(row => row.StatusId);

        assets.HasOne<AssetTypeRow>().WithMany().HasForeignKey(row => row.AssetTypeId).OnDelete(DeleteBehavior.Restrict);
        assets.HasOne<ManufacturerRow>().WithMany().HasForeignKey(row => row.ManufacturerId).OnDelete(DeleteBehavior.Restrict);
        assets.HasOne<AssetModelRow>().WithMany().HasForeignKey(row => row.AssetModelId).OnDelete(DeleteBehavior.Restrict);
        assets.HasOne<AssetLocationRow>().WithMany().HasForeignKey(row => row.LocationId).OnDelete(DeleteBehavior.Restrict);
        assets.HasOne<AssetStatusRow>().WithMany().HasForeignKey(row => row.StatusId).OnDelete(DeleteBehavior.Restrict);
        assets.Property(row => row.Id).HasColumnName(AssetFields.Id);
        assets.Property(row => row.Name).HasColumnName(AssetFields.Name);
        assets.Property(row => row.CreatedAt).HasColumnName(AssetFields.CreatedAt);
        assets.Property(row => row.UpdatedAt).HasColumnName(AssetFields.UpdatedAt);
        assets.Property(row => row.AssetTypeId).HasColumnName(AssetFields.AssetTypeId);
        assets.Property(row => row.AssetTag).HasColumnName(AssetFields.AssetTag);
        assets.Property(row => row.SerialNumber).HasColumnName(AssetFields.SerialNumber);
        assets.Property(row => row.ManufacturerId).HasColumnName(AssetFields.ManufacturerId);
        assets.Property(row => row.AssetModelId).HasColumnName(AssetFields.AssetModelId);
        assets.Property(row => row.LocationId).HasColumnName(AssetFields.LocationId);
        assets.Property(row => row.StatusId).HasColumnName(AssetFields.StatusId);
        assets.Property(row => row.Lifecycle).HasColumnName(AssetFields.Lifecycle);
        assets.Property(row => row.PurchaseDate).HasColumnName(AssetFields.PurchaseDate);
        assets.Property(row => row.PurchasePrice).HasColumnName(AssetFields.PurchasePrice);
        assets.Property(row => row.Condition).HasColumnName(AssetFields.Condition);
        assets.Property(row => row.Notes).HasColumnName(AssetFields.Notes);
    }

    private static void Catalogue<T>(ModelBuilder model, string table) where T : AssetCatalogueRow
    {
        var entity = model.Entity<T>();
        entity.HasBaseType((Type?)null);
        entity.ToTable(table);
        entity.HasKey(row => row.Id);
        entity.Property(row => row.Id).ValueGeneratedNever().HasColumnName(AssetFields.Id);
        entity.Property(row => row.Code).IsRequired().UseCollation(AssetsStorage.OrdinalCollation).HasColumnName(AssetFields.Code);
        entity.Property(row => row.Name).IsRequired().UseCollation(AssetsStorage.OrdinalCollation).HasColumnName(AssetFields.Name);
        entity.Property(row => row.Active).HasColumnName(AssetFields.Active);
        entity.Property(row => row.CreatedAt).HasColumnName(AssetFields.CreatedAt);
        entity.Property(row => row.UpdatedAt).HasColumnName(AssetFields.UpdatedAt);
        if (typeof(T) != typeof(AssetModelRow)) entity.HasIndex(row => row.Code).IsUnique();
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder) => ConfigureModel(modelBuilder);
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Initial DDL generated from the frozen EF PostgreSQL model, including
        // exact key/index names. Do not regenerate this migration after release.
        migrationBuilder.Sql($"""
            CREATE TABLE {AssetsStorage.Locations} (
                {AssetFields.Id} uuid NOT NULL,
                {AssetFields.Code} text COLLATE "{AssetsStorage.OrdinalCollation}" NOT NULL,
                {AssetFields.Name} text COLLATE "{AssetsStorage.OrdinalCollation}" NOT NULL,
                {AssetFields.Active} boolean NOT NULL,
                "{AssetFields.CreatedAt}" timestamp with time zone NOT NULL,
                "{AssetFields.UpdatedAt}" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_assets_locations" PRIMARY KEY ({AssetFields.Id})
            );


            CREATE TABLE {AssetsStorage.Manufacturers} (
                {AssetFields.Id} uuid NOT NULL,
                {AssetFields.Code} text COLLATE "{AssetsStorage.OrdinalCollation}" NOT NULL,
                {AssetFields.Name} text COLLATE "{AssetsStorage.OrdinalCollation}" NOT NULL,
                {AssetFields.Active} boolean NOT NULL,
                "{AssetFields.CreatedAt}" timestamp with time zone NOT NULL,
                "{AssetFields.UpdatedAt}" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_assets_manufacturers" PRIMARY KEY ({AssetFields.Id})
            );


            CREATE TABLE {AssetsStorage.Statuses} (
                {AssetFields.Id} uuid NOT NULL,
                {AssetFields.Code} text COLLATE "{AssetsStorage.OrdinalCollation}" NOT NULL,
                {AssetFields.Name} text COLLATE "{AssetsStorage.OrdinalCollation}" NOT NULL,
                {AssetFields.Active} boolean NOT NULL,
                "{AssetFields.CreatedAt}" timestamp with time zone NOT NULL,
                "{AssetFields.UpdatedAt}" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_assets_statuses" PRIMARY KEY ({AssetFields.Id})
            );


            CREATE TABLE {AssetsStorage.Types} (
                {AssetFields.Id} uuid NOT NULL,
                {AssetFields.Description} text,
                {AssetFields.Code} text COLLATE "{AssetsStorage.OrdinalCollation}" NOT NULL,
                {AssetFields.Name} text COLLATE "{AssetsStorage.OrdinalCollation}" NOT NULL,
                {AssetFields.Active} boolean NOT NULL,
                "{AssetFields.CreatedAt}" timestamp with time zone NOT NULL,
                "{AssetFields.UpdatedAt}" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_assets_types" PRIMARY KEY ({AssetFields.Id})
            );


            CREATE TABLE {AssetsStorage.Models} (
                {AssetFields.Id} uuid NOT NULL,
                "{AssetFields.ManufacturerId}" uuid NOT NULL,
                {AssetFields.Code} text COLLATE "{AssetsStorage.OrdinalCollation}" NOT NULL,
                {AssetFields.Name} text COLLATE "{AssetsStorage.OrdinalCollation}" NOT NULL,
                {AssetFields.Active} boolean NOT NULL,
                "{AssetFields.CreatedAt}" timestamp with time zone NOT NULL,
                "{AssetFields.UpdatedAt}" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_assets_models" PRIMARY KEY ({AssetFields.Id}),
                CONSTRAINT "FK_assets_models_assets_manufacturers_manufacturerId" FOREIGN KEY ("{AssetFields.ManufacturerId}") REFERENCES {AssetsStorage.Manufacturers} ({AssetFields.Id}) ON DELETE RESTRICT
            );


            CREATE TABLE {AssetsStorage.Assets} (
                {AssetFields.Id} uuid NOT NULL,
                {AssetFields.Name} text COLLATE "{AssetsStorage.OrdinalCollation}" NOT NULL,
                "{AssetFields.AssetTypeId}" uuid NOT NULL,
                "{AssetFields.AssetTag}" text COLLATE "{AssetsStorage.OrdinalCollation}",
                "{AssetFields.SerialNumber}" text,
                "{AssetFields.ManufacturerId}" uuid,
                "{AssetFields.AssetModelId}" uuid,
                "{AssetFields.LocationId}" uuid,
                "{AssetFields.StatusId}" uuid NOT NULL,
                {AssetFields.Lifecycle} text COLLATE "{AssetsStorage.OrdinalCollation}" NOT NULL,
                "{AssetFields.PurchaseDate}" date,
                "{AssetFields.PurchasePrice}" numeric({AssetLimits.PricePrecision},{AssetLimits.PriceScale}),
                {AssetFields.Condition} smallint,
                {AssetFields.Notes} text,
                "{AssetFields.CreatedAt}" timestamp with time zone NOT NULL,
                "{AssetFields.UpdatedAt}" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_assets_assets" PRIMARY KEY ({AssetFields.Id}),
                CONSTRAINT {AssetsStorage.ConditionConstraint} CHECK ("{AssetFields.Condition}" BETWEEN 0 AND {AssetLimits.MaximumCondition}),
                CONSTRAINT "FK_assets_assets_assets_locations_locationId" FOREIGN KEY ("{AssetFields.LocationId}") REFERENCES {AssetsStorage.Locations} ({AssetFields.Id}) ON DELETE RESTRICT,
                CONSTRAINT "FK_assets_assets_assets_manufacturers_manufacturerId" FOREIGN KEY ("{AssetFields.ManufacturerId}") REFERENCES {AssetsStorage.Manufacturers} ({AssetFields.Id}) ON DELETE RESTRICT,
                CONSTRAINT "FK_assets_assets_assets_models_assetModelId" FOREIGN KEY ("{AssetFields.AssetModelId}") REFERENCES {AssetsStorage.Models} ({AssetFields.Id}) ON DELETE RESTRICT,
                CONSTRAINT "FK_assets_assets_assets_statuses_statusId" FOREIGN KEY ("{AssetFields.StatusId}") REFERENCES {AssetsStorage.Statuses} ({AssetFields.Id}) ON DELETE RESTRICT,
                CONSTRAINT "FK_assets_assets_assets_types_assetTypeId" FOREIGN KEY ("{AssetFields.AssetTypeId}") REFERENCES {AssetsStorage.Types} ({AssetFields.Id}) ON DELETE RESTRICT
            );


            CREATE INDEX "IX_assets_assets_assetModelId" ON {AssetsStorage.Assets} ("{AssetFields.AssetModelId}");


            CREATE UNIQUE INDEX "IX_assets_assets_assetTag" ON {AssetsStorage.Assets} ("{AssetFields.AssetTag}");


            CREATE INDEX "IX_assets_assets_assetTypeId" ON {AssetsStorage.Assets} ("{AssetFields.AssetTypeId}");


            CREATE INDEX "IX_assets_assets_locationId" ON {AssetsStorage.Assets} ("{AssetFields.LocationId}");


            CREATE INDEX "IX_assets_assets_manufacturerId" ON {AssetsStorage.Assets} ("{AssetFields.ManufacturerId}");


            CREATE INDEX "IX_assets_assets_statusId" ON {AssetsStorage.Assets} ("{AssetFields.StatusId}");


            CREATE UNIQUE INDEX "IX_assets_locations_code" ON {AssetsStorage.Locations} ({AssetFields.Code});


            CREATE UNIQUE INDEX "IX_assets_manufacturers_code" ON {AssetsStorage.Manufacturers} ({AssetFields.Code});


            CREATE UNIQUE INDEX "IX_assets_models_manufacturerId_code" ON {AssetsStorage.Models} ("{AssetFields.ManufacturerId}", {AssetFields.Code});


            CREATE UNIQUE INDEX "IX_assets_statuses_code" ON {AssetsStorage.Statuses} ({AssetFields.Code});


            CREATE UNIQUE INDEX "IX_assets_types_code" ON {AssetsStorage.Types} ({AssetFields.Code});
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(AssetsStorage.Assets);
        migrationBuilder.DropTable(AssetsStorage.Models);
        migrationBuilder.DropTable(AssetsStorage.Types);
        migrationBuilder.DropTable(AssetsStorage.Locations);
        migrationBuilder.DropTable(AssetsStorage.Statuses);
        migrationBuilder.DropTable(AssetsStorage.Manufacturers);
    }
}
