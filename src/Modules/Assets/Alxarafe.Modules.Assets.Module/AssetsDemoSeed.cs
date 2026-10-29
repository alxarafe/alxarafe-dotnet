using System.Globalization;
using Alxarafe.Resources;
using Microsoft.EntityFrameworkCore;

namespace Alxarafe.Modules.Assets;

// Explicitly owned identities only. Natural-key collisions fail the transaction;
// they never adopt or overwrite an existing user-created record.
public static class AssetsDemoSeed
{
    public const string EnabledKey = "Assets:DemoData:Enabled";
    private const byte RepairConditionThreshold = 50;
    private const int AssetIdentityStart = 1000;
    private const int ValueIdentityStart = 2000;
    private const int AssignmentIdentityStart = 3000;
    private static readonly DateTimeOffset Created = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Snapshot = new(2026, 6, 30, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset HistoryStart = new(2024, 8, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset HistoryEnd = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset CurrentStart = new(2025, 2, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid Monitor = Identity(101), Phone = Identity(102), Printer = Identity(103), Server = Identity(104);
    private static readonly Guid Lenovo = Identity(201), Hp = Identity(202), Samsung = Identity(203), Apple = Identity(204);
    private static readonly Guid ThinkPad = Identity(301), EliteBook = Identity(302), MacBook = Identity(303), UltraSharp = Identity(304),
        ViewFinity = Identity(305), E24 = Identity(306), IPhone = Identity(307), Galaxy = Identity(308), LaserJet = Identity(309),
        PowerEdge = Identity(310), ProLiant = Identity(311), ThinkSystem = Identity(312);
    private static readonly Guid Warehouse = Identity(401), Lab = Identity(402), Remote = Identity(403);
    private static readonly Guid Ram = Identity(501), Encrypted = Identity(502), Warranty = Identity(503);

    internal static async Task InitializeAsync(AssetsDbContext db, CancellationToken ct)
    {
        await using var transaction = await AssetsPersistence.Mutation(db, ct);
        foreach (var row in new AssetTypeRow[]
        {
            new() { Id = Monitor, Code = "MONITOR", Name = "Monitor" },
            new() { Id = Phone, Code = "PHONE", Name = "Phone" },
            new() { Id = Printer, Code = "PRINTER", Name = "Printer" },
            new() { Id = Server, Code = "SERVER", Name = "Server" }
        }) await Catalogue(db, row, ct);
        foreach (var row in new ManufacturerRow[]
        {
            new() { Id = Lenovo, Code = "LENOVO", Name = "Lenovo" },
            new() { Id = Hp, Code = "HP", Name = "HP" },
            new() { Id = Samsung, Code = "SAMSUNG", Name = "Samsung" },
            new() { Id = Apple, Code = "APPLE", Name = "Apple" }
        }) await Catalogue(db, row, ct);
        foreach (var row in new AssetLocationRow[]
        {
            new() { Id = Warehouse, Code = "WAREHOUSE", Name = "Warehouse" },
            new() { Id = Lab, Code = "TECHNICAL_LAB", Name = "Technical lab" },
            new() { Id = Remote, Code = "REMOTE_OFFICE", Name = "Remote office" }
        }) await Catalogue(db, row, ct);
        await AssetsPersistence.Save(db, ct);
        foreach (var row in new AssetModelRow[]
        {
            Model(ThinkPad, Lenovo, "THINKPAD_T14", "ThinkPad T14"),
            Model(EliteBook, Hp, "ELITEBOOK_840", "EliteBook 840"),
            Model(MacBook, Apple, "MACBOOK_AIR_M2", "MacBook Air M2"),
            Model(UltraSharp, AssetsSeed.Dell, "ULTRASHARP_U2424H", "UltraSharp U2424H"),
            Model(ViewFinity, Samsung, "VIEWFINITY_S6", "ViewFinity S6"),
            Model(E24, Hp, "E24_G5", "E24 G5"),
            Model(IPhone, Apple, "IPHONE_15", "iPhone 15"),
            Model(Galaxy, Samsung, "GALAXY_A55", "Galaxy A55"),
            Model(LaserJet, Hp, "LASERJET_PRO_4002", "LaserJet Pro 4002"),
            Model(PowerEdge, AssetsSeed.Dell, "POWEREDGE_R350", "PowerEdge R350"),
            Model(ProLiant, Hp, "PROLIANT_DL20", "ProLiant DL20"),
            Model(ThinkSystem, Lenovo, "THINKSYSTEM_SR250", "ThinkSystem SR250")
        }) await Catalogue(db, row, ct);
        var definitions = new AssetFieldDefinitionRow[]
        {
            new() { Id = Ram, Code = "RAM_GB", Name = "Memory (GB)", FieldType = ScalarType.Int32, Required = true, Minimum = "4", Maximum = "128", DisplayOrder = 1 },
            new() { Id = Encrypted, Code = "ENCRYPTED", Name = "Disk encryption", FieldType = ScalarType.Boolean, Required = true, DisplayOrder = 2 },
            new() { Id = Warranty, Code = "WARRANTY_UNTIL", Name = "Warranty until", FieldType = ScalarType.Date, DisplayOrder = 3 }
        };
        foreach (var field in definitions)
        {
            field.AssetTypeId = AssetsSeed.Laptop; field.CreatedAt = Created; field.UpdatedAt = Snapshot;
            await Upsert(db, field, ct);
        }
        await AssetsPersistence.Save(db, ct);
        var statuses = await db.Statuses.ToDictionaryAsync(row => row.Code, ct);
        var assets = Manifest();
        var staticDefinition = new AssetsDefinitions().GetResourceDefinitions().Single(row => row.Id == AssetResources.Asset);
        foreach (var row in assets)
        {
            string status = row.Lifecycle == AssetLifecycle.Retired ? AssetStatusCodes.Retired
                : row.Condition < RepairConditionThreshold ? AssetStatusCodes.InRepair
                : row.LocationId == Warehouse || row.Lifecycle == AssetLifecycle.Draft ? AssetStatusCodes.InStock : AssetStatusCodes.Active;
            row.StatusId = statuses[status].Id;
            await AssetsPersistence.Reference<AssetStatusRow>(db, row.StatusId, AssetResources.Status, true, ct);
            await AssetRepository.ValidateReferences(db, row, null, ct);
            // Use the same static completion validation without changing canonical lifecycle.
            string lifecycle = row.Lifecycle;
            row.Lifecycle = AssetLifecycle.Draft; AssetRules.Complete(row, staticDefinition); row.Lifecycle = lifecycle;
            await Upsert(db, row, ct);
        }
        await AssetsPersistence.Save(db, ct);
        // Six completed laptops satisfy both requirements. Draft 7 has no values;
        // draft 8 deliberately has RAM only. Optional warranties are not universal.
        foreach (var (number, ram, encrypted, warranty) in new (int, int, bool, string?)[]
        {
            (1, 16, true, "2026-12-31"), (2, 8, true, "2026-06-30"), (3, 16, false, null),
            (4, 32, true, "2027-01-31"), (5, 8, true, "2026-09-30"), (6, 8, false, null)
        })
        {
            await Value(db, number, 1, Ram, ram.ToString(CultureInfo.InvariantCulture), ct);
            await Value(db, number, 2, Encrypted, encrypted ? "true" : "false", ct);
            if (warranty is not null) await Value(db, number, 3, Warranty, '"' + warranty + '"', ct);
        }
        await Value(db, 8, 1, Ram, "16", ct);
        await AssetsPersistence.Save(db, ct);
        foreach (var field in definitions) await AssetDynamicFields.Evolution(db, field, ct);
        foreach (var asset in assets.Where(row => row.Lifecycle != AssetLifecycle.Draft))
            await AssetDynamicFields.RequireComplete(db, asset, ct);

        // Rebuild only the twelve named history rows. Unknown/user-owned history
        // is preserved; conflicting custody causes rollback rather than deletion.
        var assignments = Assignments();
        var assignmentIds = assignments.Select(row => row.Id).ToArray();
        await db.Assignments.Where(row => assignmentIds.Contains(row.Id)).ExecuteDeleteAsync(ct);
        foreach (var row in assignments)
        {
            await AssetsPersistence.Reference<AssetLocationRow>(db, row.TargetId, AssetResources.Location, true, ct);
            db.Assignments.Add(row);
        }
        await AssetsPersistence.Save(db, ct);
        await transaction.CommitAsync(ct);
    }

    private static AssetRow[] Manifest() =>
    [
        Asset(1, AssetsSeed.Laptop, "ERB-LT-001", "Operations laptop", AssetsSeed.Dell, AssetsSeed.Latitude, AssetsSeed.Office, AssetLifecycle.Ready, 90, 1249m, new(2024, 2, 15)),
        Asset(2, AssetsSeed.Laptop, "ERB-LT-002", "Reserve laptop", Lenovo, ThinkPad, Warehouse, AssetLifecycle.Ready, 82, 1120m, new(2024, 4, 10)),
        Asset(3, AssetsSeed.Laptop, "ERB-LT-003", "Lab service laptop", Hp, EliteBook, Lab, AssetLifecycle.Ready, 40, 1180m, new(2024, 3, 20)),
        Asset(4, AssetsSeed.Laptop, "ERB-LT-004", "Remote design laptop", Apple, MacBook, Remote, AssetLifecycle.Ready, 96, 1499m, new(2025, 1, 10)),
        Asset(5, AssetsSeed.Laptop, "ERB-LT-005", "Reception laptop", AssetsSeed.Dell, AssetsSeed.Latitude, AssetsSeed.Office, AssetLifecycle.Ready, 75, 1199m, new(2024, 5, 6)),
        Asset(6, AssetsSeed.Laptop, "ERB-LT-006", "Retired training laptop", Lenovo, ThinkPad, Warehouse, AssetLifecycle.Retired, 30, 990m, new(2024, 1, 18)),
        Asset(7, AssetsSeed.Laptop, null, "New laptop awaiting details", null, null, null, AssetLifecycle.Draft, null, null, null),
        Asset(8, AssetsSeed.Laptop, "ERB-LT-008", "Laptop awaiting encryption details", Hp, EliteBook, null, AssetLifecycle.Draft, null, 1250m, new(2026, 5, 12)),
        Asset(9, Monitor, "ERB-MN-001", "Operations display", AssetsSeed.Dell, UltraSharp, AssetsSeed.Office, AssetLifecycle.Ready, 92, 279m, new(2024, 3, 1)),
        Asset(10, Monitor, "ERB-MN-002", "Reserve widescreen display", Samsung, ViewFinity, Warehouse, AssetLifecycle.Ready, 88, 349m, new(2025, 6, 2)),
        Asset(11, Monitor, "ERB-MN-003", "Display awaiting backlight repair", Hp, E24, Lab, AssetLifecycle.Ready, 45, 210m, new(2024, 7, 9)),
        Asset(12, Monitor, "ERB-MN-004", "Remote office display", AssetsSeed.Dell, UltraSharp, Remote, AssetLifecycle.Ready, 84, 269m, new(2024, 6, 14)),
        Asset(13, Monitor, "ERB-MN-005", "Display awaiting inspection", Samsung, ViewFinity, null, AssetLifecycle.Draft, null, null, null),
        Asset(14, Phone, "ERB-PH-001", "Operations duty phone", Apple, IPhone, AssetsSeed.Office, AssetLifecycle.Ready, 95, 799m, new(2025, 1, 16)),
        Asset(15, Phone, "ERB-PH-002", "Remote office duty phone", Samsung, Galaxy, Remote, AssetLifecycle.Ready, 78, 399m, new(2025, 3, 22)),
        Asset(16, Phone, "ERB-PH-003", "Retired demonstration phone", Apple, IPhone, null, AssetLifecycle.Retired, 35, 749m, new(2024, 6, 10)),
        Asset(17, Phone, null, "Phone awaiting registration", null, null, null, AssetLifecycle.Draft, null, null, null),
        Asset(18, Printer, "ERB-PR-001", "Main office document printer", Hp, LaserJet, AssetsSeed.Office, AssetLifecycle.Ready, 80, 329m, new(2024, 11, 5)),
        Asset(19, Printer, "ERB-PR-002", "Warehouse printer awaiting repair", Hp, LaserJet, Warehouse, AssetLifecycle.Ready, 40, 315m, new(2024, 8, 19)),
        Asset(20, Printer, "ERB-PR-003", "Printer awaiting installation", Hp, LaserJet, null, AssetLifecycle.Draft, null, 359m, new(2026, 4, 3)),
        Asset(21, Server, "ERB-SV-001", "Technical lab application server", AssetsSeed.Dell, PowerEdge, Lab, AssetLifecycle.Ready, 85, 2699m, new(2024, 3, 12)),
        Asset(22, Server, "ERB-SV-002", "Reserve integration server", Hp, ProLiant, Warehouse, AssetLifecycle.Ready, 90, 2199m, new(2025, 7, 21)),
        Asset(23, Server, "ERB-SV-003", "Remote office file server", Lenovo, ThinkSystem, Remote, AssetLifecycle.Ready, 70, 2399m, new(2024, 9, 30)),
        Asset(24, Server, "ERB-SV-004", "Retired training server", AssetsSeed.Dell, PowerEdge, Warehouse, AssetLifecycle.Retired, 20, 2499m, new(2024, 1, 25))
    ];

    private static AssetRow Asset(int number, Guid type, string? tag, string name, Guid? manufacturer, Guid? model, Guid? location,
        string lifecycle, byte? condition, decimal? price, DateOnly? purchase) => new()
        {
            Id = Identity(AssetIdentityStart + number),
            AssetTypeId = type,
            AssetTag = tag,
            Name = name,
            ManufacturerId = manufacturer,
            AssetModelId = model,
            LocationId = location,
            Lifecycle = lifecycle,
            Condition = condition,
            PurchasePrice = price,
            PurchaseDate = purchase,
            SerialNumber = lifecycle == AssetLifecycle.Draft ? null : $"DEMO-SN-{number:D3}",
            Notes = lifecycle == AssetLifecycle.Draft ? "Fictional demo record; registration is intentionally incomplete."
                : lifecycle == AssetLifecycle.Retired ? "Fictional demo record; withdrawn from service after inspection."
                : condition < RepairConditionThreshold ? "Fictional demo record; inspection identified a repair requirement."
                : "Fictional demo equipment; available for application demonstrations.",
            CreatedAt = Created,
            UpdatedAt = Snapshot
        };
    private static AssetModelRow Model(Guid id, Guid manufacturer, string code, string name) =>
        new() { Id = id, ManufacturerId = manufacturer, Code = code, Name = name };
    private static async Task Catalogue<T>(AssetsDbContext db, T row, CancellationToken ct) where T : AssetCatalogueRow
    {
        row.CreatedAt = Created; row.UpdatedAt = Snapshot;
        await Upsert(db, row, ct);
    }
    private static async Task Value(AssetsDbContext db, int asset, int slot, Guid definition, string json, CancellationToken ct)
    {
        var field = await db.FieldDefinitions.SingleAsync(row => row.Id == definition, ct);
        if (!AssetDynamicSchema.Accepts(field, json)) throw new InvalidOperationException("Invalid canonical demo dynamic value.");
        await Upsert(db, new AssetFieldValueRow
        {
            Id = Identity(ValueIdentityStart + asset * 10 + slot),
            AssetId = Identity(AssetIdentityStart + asset),
            FieldDefinitionId = definition,
            AssetTypeId = AssetsSeed.Laptop,
            ValueJson = json,
            CreatedAt = Created,
            UpdatedAt = Snapshot
        }, ct);
    }
    private static AssetAssignmentRow[] Assignments()
    {
        var rows = new List<AssetAssignmentRow>();
        foreach (var (asset, location) in new (int, Guid)[]
        { (1, AssetsSeed.Office), (2, Warehouse), (4, Remote), (5, AssetsSeed.Office), (9, AssetsSeed.Office), (12, Remote), (14, AssetsSeed.Office), (21, Lab) })
            rows.Add(Assignment(rows.Count + 1, asset, location, CurrentStart, null));
        foreach (var (asset, location) in new (int, Guid)[] { (1, Warehouse), (3, Lab), (6, AssetsSeed.Office), (16, AssetsSeed.Office) })
            rows.Add(Assignment(rows.Count + 1, asset, location, HistoryStart, HistoryEnd));
        return rows.ToArray();
    }
    private static AssetAssignmentRow Assignment(int number, int asset, Guid location, DateTimeOffset start, DateTimeOffset? end) => new()
    {
        Id = Identity(AssignmentIdentityStart + number),
        AssetId = Identity(AssetIdentityStart + asset),
        TargetType = AssetAssignmentTargets.Location,
        TargetId = location,
        StartedAt = start,
        EndedAt = end,
        StartReason = "Demo equipment deployment",
        EndReason = end is null ? null : "Demo equipment returned",
        Notes = "Fictional location custody history",
        CreatedAt = start,
        UpdatedAt = end ?? start
    };
    private static Guid Identity(int number) => Guid.Parse("a55e7d00-0000-4000-8000-" + number.ToString("D12", CultureInfo.InvariantCulture));
    private static async Task Upsert<T>(AssetsDbContext db, T row, CancellationToken ct) where T : class
    {
        var id = db.Entry(row).Property(nameof(AssetRow.Id)).CurrentValue;
        var existing = await db.Set<T>().FindAsync([id], ct);
        if (existing is null) db.Add(row);
        else db.Entry(existing).CurrentValues.SetValues(row);
    }
}
