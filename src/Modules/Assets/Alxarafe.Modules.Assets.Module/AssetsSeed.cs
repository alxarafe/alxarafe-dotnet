using Microsoft.EntityFrameworkCore;

namespace Alxarafe.Modules.Assets;

public static class AssetsSeed
{
    public static readonly Guid Laptop = Guid.Parse("130e9201-e5a9-4739-8be0-000000000001");
    public static readonly Guid Dell = Guid.Parse("130e9201-e5a9-4739-8be0-000000000002");
    public static readonly Guid Latitude = Guid.Parse("130e9201-e5a9-4739-8be0-000000000003");
    public static readonly Guid Office = Guid.Parse("130e9201-e5a9-4739-8be0-000000000004");
    internal static async Task InitializeAsync(AssetsDbContext db, CancellationToken ct)
    {
        await using var transaction = await AssetsPersistence.Mutation(db, ct);
        await Add(db, new AssetTypeRow { Id = Laptop, Code = "LAPTOP", Name = "Laptop" }, ct);
        await Add(db, new ManufacturerRow { Id = Dell, Code = "DELL", Name = "Dell" }, ct);
        await Add(db, new AssetLocationRow { Id = Office, Code = "MAIN_OFFICE", Name = "Main office" }, ct);
        for (int index = 0; index < AssetStatusCodes.Reserved.Count; index++)
        {
            string code = AssetStatusCodes.Reserved[index];
            await Add(db, new AssetStatusRow { Id = new Guid(0x130e9201, unchecked((short)0xe5a9), 0x4739, 0x8b, 0xe0, 0, 0, 0, 0, 1, (byte)index), Code = code, Name = code }, ct);
        }
        await AssetsPersistence.Save(db, ct);
        await Add(db, new AssetModelRow { Id = Latitude, ManufacturerId = Dell, Code = "LATITUDE_5450", Name = "Latitude 5450" }, ct);
        await AssetsPersistence.Save(db, ct); await transaction.CommitAsync(ct);
    }
    private static async Task Add<T>(AssetsDbContext db, T row, CancellationToken ct) where T : AssetCatalogueRow
    {
        if (await db.Set<T>().AnyAsync(existing => existing.Id == row.Id || existing.Code == row.Code, ct)) return;
        row.CreatedAt = row.UpdatedAt = AssetsPersistence.Now(); db.Add(row);
    }
}
