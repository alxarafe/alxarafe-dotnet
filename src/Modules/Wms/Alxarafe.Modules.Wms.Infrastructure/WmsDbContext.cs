using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Alxarafe.Modules.Wms.Infrastructure;

public static class WmsStorage
{
    public const string ConnectionName = "Wms";
    public const string TestingEnvironment = "Testing";
    public const string ValidationDatabase = "alxarafe_test";
    public const string MigrationHistory = "__WmsMigrationsHistory";
    public const string Table = "wms_uoms";
    public const string IdColumn = "id";
    public const string CodeColumn = "code";
    public const string DescriptionColumn = "description";
    public const string CodeIndex = "IX_wms_uoms_code";
    public const string PrimaryKey = "PK_wms_uoms";
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

public sealed class WmsDbContext(DbContextOptions<WmsDbContext> options) : DbContext(options)
{
    public DbSet<UomRow> Uoms => Set<UomRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var uoms = modelBuilder.Entity<UomRow>();
        uoms.ToTable(WmsStorage.Table);
        uoms.HasKey(uom => uom.Id).HasName(WmsStorage.PrimaryKey);
        uoms.Property(uom => uom.Id).HasColumnName(WmsStorage.IdColumn).HasColumnType("uuid").ValueGeneratedNever();
        uoms.Property(uom => uom.Code).HasColumnName(WmsStorage.CodeColumn).HasColumnType("text")
            .UseCollation(WmsStorage.OrdinalCollation).IsRequired();
        uoms.Property(uom => uom.Description).HasColumnName(WmsStorage.DescriptionColumn).HasColumnType("text").IsRequired();
        uoms.HasIndex(uom => uom.Code).IsUnique().HasDatabaseName(WmsStorage.CodeIndex);
    }
}
