using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Alxarafe.Modules.Assets.Migrations;

[DbContext(typeof(AssetsDbContext))]
[Migration("202610270002_DynamicAssetFields")]
public sealed class DynamicAssetFields : Migration
{
    internal static void ConfigureModel(ModelBuilder model)
    {
        InitialAssets.ConfigureModel(model);
        model.Entity<AssetRow>().HasAlternateKey(row => new { row.Id, row.AssetTypeId }).HasName(AssetsStorage.AssetTypeIdentityKey);
        var definitions = model.Entity<AssetFieldDefinitionRow>();
        definitions.ToTable(AssetsStorage.FieldDefinitions);
        definitions.HasKey(row => row.Id);
        definitions.HasAlternateKey(row => new { row.Id, row.AssetTypeId });
        definitions.HasIndex(row => new { row.AssetTypeId, row.Code }).IsUnique();
        definitions.Property(row => row.Id).ValueGeneratedNever().HasColumnName(AssetFields.Id);
        definitions.Property(row => row.AssetTypeId).HasColumnName(AssetFields.AssetTypeId);
        definitions.Property(row => row.Code).IsRequired().UseCollation(AssetsStorage.OrdinalCollation).HasColumnName(AssetFields.Code);
        definitions.Property(row => row.Name).IsRequired().UseCollation(AssetsStorage.OrdinalCollation).HasColumnName(AssetFields.Name);
        definitions.Property(row => row.Description).HasColumnName(AssetFields.Description);
        definitions.Property(row => row.FieldType).HasConversion<string>().HasColumnName(AssetFields.FieldType);
        definitions.Property(row => row.Required).HasColumnName(AssetFields.Required);
        definitions.Property(row => row.Active).HasColumnName(AssetFields.Active);
        definitions.Property(row => row.DisplayOrder).HasColumnName(AssetFields.DisplayOrder);
        definitions.Property(row => row.MinimumLength).HasColumnName(AssetFields.MinimumLength);
        definitions.Property(row => row.MaximumLength).HasColumnName(AssetFields.MaximumLength);
        definitions.Property(row => row.Minimum).HasColumnName(AssetFields.Minimum);
        definitions.Property(row => row.Maximum).HasColumnName(AssetFields.Maximum);
        definitions.Property(row => row.Precision).HasColumnName(AssetFields.Precision);
        definitions.Property(row => row.Scale).HasColumnName(AssetFields.Scale);
        definitions.Property(row => row.CreatedAt).HasColumnName(AssetFields.CreatedAt);
        definitions.Property(row => row.UpdatedAt).HasColumnName(AssetFields.UpdatedAt);
        definitions.HasOne<AssetTypeRow>().WithMany().HasForeignKey(row => row.AssetTypeId).OnDelete(DeleteBehavior.Restrict);

        var values = model.Entity<AssetFieldValueRow>();
        values.ToTable(AssetsStorage.FieldValues, table => table.HasCheckConstraint(AssetsStorage.ValueNotNullConstraint,
            $"jsonb_typeof({AssetsStorage.ValueJsonColumn}) <> 'null'"));
        values.HasKey(row => row.Id);
        values.HasIndex(row => new { row.AssetId, row.FieldDefinitionId }).IsUnique();
        values.HasIndex(row => new { row.FieldDefinitionId, row.AssetTypeId });
        values.HasIndex(row => new { row.AssetId, row.AssetTypeId });
        values.Property(row => row.Id).ValueGeneratedNever().HasColumnName(AssetFields.Id);
        values.Property(row => row.AssetId).HasColumnName(AssetFields.AssetId);
        values.Property(row => row.FieldDefinitionId).HasColumnName(AssetFields.FieldDefinitionId);
        values.Property(row => row.AssetTypeId).HasColumnName(AssetFields.AssetTypeId);
        values.Property(row => row.ValueJson).IsRequired().HasColumnType("jsonb").HasColumnName(AssetsStorage.ValueJsonColumn);
        values.Property(row => row.CreatedAt).HasColumnName(AssetFields.CreatedAt);
        values.Property(row => row.UpdatedAt).HasColumnName(AssetFields.UpdatedAt);
        values.HasOne<AssetRow>().WithMany().HasForeignKey(row => new { row.AssetId, row.AssetTypeId })
            .HasPrincipalKey(row => new { row.Id, row.AssetTypeId }).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(AssetsStorage.ValueAssetForeignKey);
        values.HasOne(row => row.Definition).WithMany().HasForeignKey(row => new { row.FieldDefinitionId, row.AssetTypeId })
            .HasPrincipalKey(row => new { row.Id, row.AssetTypeId }).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(AssetsStorage.ValueDefinitionForeignKey);
    }
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => ConfigureModel(modelBuilder);
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Frozen incremental DDL generated from the PostgreSQL EF model.
        migrationBuilder.Sql($"""
            ALTER TABLE {AssetsStorage.Assets} ADD CONSTRAINT "{AssetsStorage.AssetTypeIdentityKey}" UNIQUE ({AssetFields.Id}, "{AssetFields.AssetTypeId}");

            CREATE TABLE {AssetsStorage.FieldDefinitions} (
                {AssetFields.Id} uuid NOT NULL,
                "{AssetFields.AssetTypeId}" uuid NOT NULL,
                {AssetFields.Code} text COLLATE "{AssetsStorage.OrdinalCollation}" NOT NULL,
                {AssetFields.Name} text COLLATE "{AssetsStorage.OrdinalCollation}" NOT NULL,
                {AssetFields.Description} text,
                "{AssetFields.FieldType}" text NOT NULL,
                {AssetFields.Required} boolean NOT NULL,
                {AssetFields.Active} boolean NOT NULL,
                "{AssetFields.DisplayOrder}" integer NOT NULL,
                "{AssetFields.MinimumLength}" integer,
                "{AssetFields.MaximumLength}" integer,
                {AssetFields.Minimum} text,
                {AssetFields.Maximum} text,
                {AssetFields.Precision} integer,
                {AssetFields.Scale} integer,
                "{AssetFields.CreatedAt}" timestamp with time zone NOT NULL,
                "{AssetFields.UpdatedAt}" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_assets_field_definitions" PRIMARY KEY ({AssetFields.Id}),
                CONSTRAINT "AK_assets_field_definitions_id_assetTypeId" UNIQUE ({AssetFields.Id}, "{AssetFields.AssetTypeId}"),
                CONSTRAINT "FK_assets_field_definitions_assets_types_assetTypeId" FOREIGN KEY ("{AssetFields.AssetTypeId}") REFERENCES {AssetsStorage.Types} ({AssetFields.Id}) ON DELETE RESTRICT
            );

            CREATE TABLE {AssetsStorage.FieldValues} (
                {AssetFields.Id} uuid NOT NULL,
                "{AssetFields.AssetId}" uuid NOT NULL,
                "{AssetFields.FieldDefinitionId}" uuid NOT NULL,
                "{AssetFields.AssetTypeId}" uuid NOT NULL,
                {AssetsStorage.ValueJsonColumn} jsonb NOT NULL,
                "{AssetFields.CreatedAt}" timestamp with time zone NOT NULL,
                "{AssetFields.UpdatedAt}" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_assets_field_values" PRIMARY KEY ({AssetFields.Id}),
                CONSTRAINT {AssetsStorage.ValueNotNullConstraint} CHECK (jsonb_typeof({AssetsStorage.ValueJsonColumn}) <> 'null'),
                CONSTRAINT "{AssetsStorage.ValueAssetForeignKey}" FOREIGN KEY ("{AssetFields.AssetId}", "{AssetFields.AssetTypeId}") REFERENCES {AssetsStorage.Assets} ({AssetFields.Id}, "{AssetFields.AssetTypeId}") ON DELETE RESTRICT,
                CONSTRAINT "{AssetsStorage.ValueDefinitionForeignKey}" FOREIGN KEY ("{AssetFields.FieldDefinitionId}", "{AssetFields.AssetTypeId}") REFERENCES {AssetsStorage.FieldDefinitions} ({AssetFields.Id}, "{AssetFields.AssetTypeId}") ON DELETE RESTRICT
            );

            CREATE UNIQUE INDEX "IX_assets_field_definitions_assetTypeId_code" ON {AssetsStorage.FieldDefinitions} ("{AssetFields.AssetTypeId}", {AssetFields.Code});

            CREATE INDEX "IX_assets_field_values_assetId_assetTypeId" ON {AssetsStorage.FieldValues} ("{AssetFields.AssetId}", "{AssetFields.AssetTypeId}");

            CREATE UNIQUE INDEX "IX_assets_field_values_assetId_fieldDefinitionId" ON {AssetsStorage.FieldValues} ("{AssetFields.AssetId}", "{AssetFields.FieldDefinitionId}");

            CREATE INDEX "IX_assets_field_values_fieldDefinitionId_assetTypeId" ON {AssetsStorage.FieldValues} ("{AssetFields.FieldDefinitionId}", "{AssetFields.AssetTypeId}");
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(AssetsStorage.FieldValues);
        migrationBuilder.DropTable(AssetsStorage.FieldDefinitions);
        migrationBuilder.DropUniqueConstraint(AssetsStorage.AssetTypeIdentityKey, AssetsStorage.Assets);
    }
}
