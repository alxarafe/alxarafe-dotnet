using Microsoft.EntityFrameworkCore;

namespace Alxarafe.EntityFrameworkCore;

public static class PlatformStorage
{
    public const string ConnectionName = "Platform";
    public const string ValidationDatabase = "alxarafe_test";
    public const string MigrationHistory = "__PlatformMigrationsHistory";
    public const string ModuleStatesTable = "module_states";
    public const string ModuleIdColumn = "module_id";
    public const string EnabledColumn = "enabled";
    public const string ModuleStatesPrimaryKey = "PK_module_states";
    public const string IdCollation = "C";

    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, postgres => postgres.MigrationsHistoryTable(MigrationHistory));
}

public sealed class ModuleStateRow
{
    public string ModuleId { get; set; } = string.Empty;
    public bool Enabled { get; set; }
}

public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : DbContext(options)
{
    public DbSet<ModuleStateRow> ModuleStates => Set<ModuleStateRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => ConfigureModel(modelBuilder);

    internal static void ConfigureModel(ModelBuilder modelBuilder)
    {
        var states = modelBuilder.Entity<ModuleStateRow>();
        states.ToTable(PlatformStorage.ModuleStatesTable);
        states.HasKey(state => state.ModuleId).HasName(PlatformStorage.ModuleStatesPrimaryKey);
        states.Property(state => state.ModuleId).HasColumnName(PlatformStorage.ModuleIdColumn)
            .HasColumnType("text").UseCollation(PlatformStorage.IdCollation).ValueGeneratedNever();
        states.Property(state => state.Enabled).HasColumnName(PlatformStorage.EnabledColumn).HasColumnType("boolean").IsRequired();
    }
}
