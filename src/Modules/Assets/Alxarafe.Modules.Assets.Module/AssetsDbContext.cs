using Alxarafe.Core;
using Microsoft.EntityFrameworkCore;

namespace Alxarafe.Modules.Assets;

public static class AssetsStorage
{
    public const string ConditionConstraint = "assets_condition_range";
    public const string History = "__AssetsMigrationsHistory";
    public const string Assets = "assets_assets";
    public const string Types = "assets_types";
    public const string Manufacturers = "assets_manufacturers";
    public const string Models = "assets_models";
    public const string Locations = "assets_locations";
    public const string Statuses = "assets_statuses";
    public const string AssetTypeIdentityKey = "AK_assets_assets_id_assetTypeId";
    public const string FieldDefinitions = "assets_field_definitions";
    public const string FieldValues = "assets_field_values";
    public const string ValueAssetForeignKey = "FK_assets_field_values_asset";
    public const string ValueDefinitionForeignKey = "FK_assets_field_values_definition";
    public const string ValueJsonColumn = "value";
    public const string ValueNotNullConstraint = "assets_field_value_not_null";
    public const string OrdinalCollation = "C";
    // Serializes module mutations within one tenant DB; different tenant DBs do not contend.
    public const long MutationLock = 202610270002;
    public static void Configure(DbContextOptionsBuilder options, string connection) =>
        options.UseNpgsql(connection, postgres => postgres.MigrationsHistoryTable(History));
}

public sealed class AssetsDbContext(DbContextOptions<AssetsDbContext> options) : DbContext(options)
{
    public DbSet<AssetRow> Assets => Set<AssetRow>();
    public DbSet<AssetTypeRow> Types => Set<AssetTypeRow>();
    public DbSet<ManufacturerRow> Manufacturers => Set<ManufacturerRow>();
    public DbSet<AssetModelRow> Models => Set<AssetModelRow>();
    public DbSet<AssetLocationRow> Locations => Set<AssetLocationRow>();
    public DbSet<AssetStatusRow> Statuses => Set<AssetStatusRow>();
    public DbSet<AssetFieldDefinitionRow> FieldDefinitions => Set<AssetFieldDefinitionRow>();
    public DbSet<AssetFieldValueRow> FieldValues => Set<AssetFieldValueRow>();
    protected override void OnModelCreating(ModelBuilder modelBuilder) => Migrations.DynamicAssetFields.ConfigureModel(modelBuilder);
}

public sealed class TenantAssetsDbContextFactory(ITenantDatabaseResolver resolver) : IDbContextFactory<AssetsDbContext>
{
    public AssetsDbContext CreateDbContext() => throw new InvalidOperationException("Tenant persistence requires asynchronous context creation.");
    public async Task<AssetsDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        var options = new DbContextOptionsBuilder<AssetsDbContext>();
        AssetsStorage.Configure(options, await resolver.ResolveAsync(cancellationToken));
        return new(options.Options);
    }
}
