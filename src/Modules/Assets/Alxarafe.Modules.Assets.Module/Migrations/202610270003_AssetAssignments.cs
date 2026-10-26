using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Alxarafe.Modules.Assets.Migrations;

[DbContext(typeof(AssetsDbContext))]
[Migration("202610270003_AssetAssignments")]
public sealed class AssetAssignments : Migration
{
    internal static void ConfigureModel(ModelBuilder model)
    {
        DynamicAssetFields.ConfigureModel(model);
        var assignments = model.Entity<AssetAssignmentRow>();
        assignments.ToTable(AssetsStorage.Assignments, table =>
        {
            table.HasCheckConstraint(AssetsStorage.AssignmentPeriodCheck,
                $"isfinite(\"{AssetFields.StartedAt}\") AND (\"{AssetFields.EndedAt}\" IS NULL OR (isfinite(\"{AssetFields.EndedAt}\") AND \"{AssetFields.EndedAt}\" >= \"{AssetFields.StartedAt}\"))");
            table.HasCheckConstraint(AssetsStorage.AssignmentClosureCheck,
                $"(\"{AssetFields.EndedAt}\" IS NULL AND \"{AssetFields.EndReason}\" IS NULL) OR (\"{AssetFields.EndedAt}\" IS NOT NULL AND \"{AssetFields.EndReason}\" IS NOT NULL AND length(\"{AssetFields.EndReason}\") > 0)");
            table.HasCheckConstraint(AssetsStorage.AssignmentTargetCheck, $"\"{AssetFields.TargetType}\" = '{AssetAssignmentTargets.Location}'");
        });
        assignments.HasKey(row => row.Id);
        assignments.Property(row => row.Id).ValueGeneratedNever().HasColumnName(AssetFields.Id);
        assignments.Property(row => row.AssetId).HasColumnName(AssetFields.AssetId);
        assignments.Property(row => row.TargetType).IsRequired().UseCollation(AssetsStorage.OrdinalCollation).HasColumnName(AssetFields.TargetType);
        assignments.Property(row => row.TargetId).HasColumnName(AssetFields.TargetId);
        assignments.Property(row => row.StartedAt).HasColumnName(AssetFields.StartedAt);
        assignments.Property(row => row.EndedAt).HasColumnName(AssetFields.EndedAt);
        assignments.Property(row => row.StartReason).HasColumnName(AssetFields.StartReason);
        assignments.Property(row => row.EndReason).HasColumnName(AssetFields.EndReason);
        assignments.Property(row => row.Notes).HasColumnName(AssetFields.Notes);
        assignments.Property(row => row.CreatedAt).HasColumnName(AssetFields.CreatedAt);
        assignments.Property(row => row.UpdatedAt).HasColumnName(AssetFields.UpdatedAt);
        assignments.HasIndex(row => row.AssetId).IsUnique().HasFilter($"\"{AssetFields.EndedAt}\" IS NULL").HasDatabaseName(AssetsStorage.CurrentAssignmentIndex);
        assignments.HasIndex(row => new { row.AssetId, row.StartedAt, row.Id }).HasDatabaseName(AssetsStorage.AssignmentChronologyIndex);
        assignments.HasIndex(row => row.TargetId);
        assignments.HasOne<AssetRow>().WithMany().HasForeignKey(row => row.AssetId).OnDelete(DeleteBehavior.Restrict).HasConstraintName(AssetsStorage.AssignmentAssetForeignKey);
        assignments.HasOne<AssetLocationRow>().WithMany().HasForeignKey(row => row.TargetId).OnDelete(DeleteBehavior.Restrict).HasConstraintName(AssetsStorage.AssignmentTargetForeignKey);
    }
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => ConfigureModel(modelBuilder);
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Provider-generated table/index DDL plus the module-owned native range
        // exclusion. No temporal framework or public history mutation API.
        migrationBuilder.Sql($"""
            CREATE EXTENSION IF NOT EXISTS {AssetsStorage.AssignmentGiSTExtension} WITH SCHEMA {AssetsStorage.AssignmentGiSTSchema};
            CREATE TABLE {AssetsStorage.Assignments} (
                {AssetFields.Id} uuid NOT NULL,
                "{AssetFields.AssetId}" uuid NOT NULL,
                "{AssetFields.TargetType}" text COLLATE "{AssetsStorage.OrdinalCollation}" NOT NULL,
                "{AssetFields.TargetId}" uuid NOT NULL,
                "{AssetFields.StartedAt}" timestamp with time zone NOT NULL,
                "{AssetFields.EndedAt}" timestamp with time zone,
                "{AssetFields.StartReason}" text,
                "{AssetFields.EndReason}" text,
                {AssetFields.Notes} text,
                "{AssetFields.CreatedAt}" timestamp with time zone NOT NULL,
                "{AssetFields.UpdatedAt}" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_assets_assignments" PRIMARY KEY ({AssetFields.Id}),
                CONSTRAINT {AssetsStorage.AssignmentClosureCheck} CHECK (("{AssetFields.EndedAt}" IS NULL AND "{AssetFields.EndReason}" IS NULL) OR ("{AssetFields.EndedAt}" IS NOT NULL AND "{AssetFields.EndReason}" IS NOT NULL AND length("{AssetFields.EndReason}") > 0)),
                CONSTRAINT {AssetsStorage.AssignmentTargetCheck} CHECK ("{AssetFields.TargetType}" = '{AssetAssignmentTargets.Location}'),
                CONSTRAINT {AssetsStorage.AssignmentPeriodCheck} CHECK (isfinite("{AssetFields.StartedAt}") AND ("{AssetFields.EndedAt}" IS NULL OR (isfinite("{AssetFields.EndedAt}") AND "{AssetFields.EndedAt}" >= "{AssetFields.StartedAt}"))),
                CONSTRAINT "{AssetsStorage.AssignmentAssetForeignKey}" FOREIGN KEY ("{AssetFields.AssetId}") REFERENCES {AssetsStorage.Assets} ({AssetFields.Id}) ON DELETE RESTRICT,
                CONSTRAINT "{AssetsStorage.AssignmentTargetForeignKey}" FOREIGN KEY ("{AssetFields.TargetId}") REFERENCES {AssetsStorage.Locations} ({AssetFields.Id}) ON DELETE RESTRICT
            );

            CREATE INDEX "{AssetsStorage.AssignmentChronologyIndex}" ON {AssetsStorage.Assignments} ("{AssetFields.AssetId}", "{AssetFields.StartedAt}", {AssetFields.Id});

            CREATE UNIQUE INDEX "{AssetsStorage.CurrentAssignmentIndex}" ON {AssetsStorage.Assignments} ("{AssetFields.AssetId}") WHERE "{AssetFields.EndedAt}" IS NULL;

            CREATE INDEX "IX_assets_assignments_targetId" ON {AssetsStorage.Assignments} ("{AssetFields.TargetId}");
            ALTER TABLE {AssetsStorage.Assignments} ADD CONSTRAINT "{AssetsStorage.AssignmentOverlapConstraint}"
                EXCLUDE USING gist ("{AssetFields.AssetId}" {AssetsStorage.AssignmentGiSTSchema}.{AssetsStorage.AssignmentUuidOperatorClass} WITH =,
                    tstzrange("{AssetFields.StartedAt}", "{AssetFields.EndedAt}", '{AssetsStorage.AssignmentRangeBounds}') WITH &&);
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql($"""
            DO $$ BEGIN
                IF EXISTS (SELECT 1 FROM {AssetsStorage.Assignments}) THEN
                    RAISE EXCEPTION 'Asset assignment history prevents destructive downgrade';
                END IF;
            END $$;
            """);
        migrationBuilder.DropTable(AssetsStorage.Assignments);
        // btree_gist is shared database infrastructure; never drop the extension.
    }
}
