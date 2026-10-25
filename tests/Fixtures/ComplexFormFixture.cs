using Alxarafe.Resources;

namespace Alxarafe.ResourceTestFixtures;

internal static class ComplexFormFixture
{
    internal const string Id = "fixture.form-item";
    internal const string ModuleId = "fixture.module";
    internal const string Version = "1.0.0";
    internal const string Duplicate = "item.duplicate";
    internal const string Recount = "inventory.recount";
    internal const string Move = "logistics.move";
    internal const string Export = "item.export";
    private static ResourceNumber Number(string text) => ResourceNumber.Parse(text);
    internal static ResourceDefinition Create(string moduleId = ModuleId, ResourceFormDefinition? form = null, bool includePresentation = true, bool reverseDefinitions = false)
    {
        FieldDefinition[] fields =
        [new("id", ScalarType.Uuid, readOnly: true, immutable: true),
            new("code", ScalarType.String, required: true, immutable: true, constraints: new TextConstraints(1, 30, "^[A-Z0-9-]+$"), capabilities: new(true, true)),
            new("name", ScalarType.String, required: true, constraints: new TextConstraints(1, 200), capabilities: new(true, true, true)),
            new("description", ScalarType.Text, nullable: true, constraints: new TextConstraints(MaximumLength: 2000)),
            new("active", ScalarType.Boolean, defaultValue: new BooleanValue(true)),
            new("itemType", ScalarType.Enum, defaultValue: new TextValue("stock"), enumValues: ["stock", "service"]),
            new("categoryId", ScalarType.Uuid, nullable: true),
            new("weight", ScalarType.Decimal, constraints: new DecimalConstraints(10, 3, new NumericConstraints(Minimum: Number("0"), ExclusiveMinimum: true))),
            new("length", ScalarType.UInt16, constraints: new NumericConstraints(Maximum: Number("10000"))),
            new("width", ScalarType.UInt16), new("height", ScalarType.UInt16),
            new("warehouseId", ScalarType.Uuid, nullable: true), new("locationId", ScalarType.Uuid, nullable: true),
            new("batchTracked", ScalarType.Boolean), new("serialTracked", ScalarType.Boolean),
            new("minStock", ScalarType.UInt32, constraints: new NumericConstraints(Maximum: Number("1000000"))),
            new("maxStock", ScalarType.UInt32, constraints: new NumericConstraints(Maximum: Number("1000000"))),
            new("quantity", ScalarType.Decimal, readOnly: true, constraints: new DecimalConstraints(18, 3, new NumericConstraints(Minimum: Number("0")))),
            new("reorderPoint", ScalarType.Decimal, constraints: new DecimalConstraints(18, 3, new NumericConstraints(Minimum: Number("0")))),
            new("cost", ScalarType.Decimal, defaultValue: new NumericValue(Number("0")), constraints: new DecimalConstraints(18, 4, new NumericConstraints(Minimum: Number("0"), Maximum: Number("100000000")))),
            new("currency", ScalarType.Enum, enumValues: ["EUR", "USD"]),
            new("stockUnit", ScalarType.String, constraints: new TextConstraints(MaximumLength: 10)), new("stockDate", ScalarType.Date, nullable: true),
            new("allowNegative", ScalarType.Boolean, defaultValue: new BooleanValue(false)), new("validFrom", ScalarType.Date),
            new("cutoff", ScalarType.Time, defaultValue: new TimeValue(new TimeOnly(17, 0))), new("parentId", ScalarType.Uuid, nullable: true),
            new("externalCode", ScalarType.String, readOnly: true), new("createdAt", ScalarType.DateTime, readOnly: true), new("updatedAt", ScalarType.DateTime, readOnly: true),
            new("internalToken", ScalarType.String, annotations: [new("UI", "Hidden", new AnnotationBoolean(true))]),
            new("attachment", ScalarType.Binary, nullable: true, annotations: [new("UI", "FormExcluded", new AnnotationBoolean(true))])];
        ActionDefinition[] actions = [new(Duplicate, ActionScope.Entity, "item.manage"), new(Recount, ActionScope.Entity, "item.manage"),
            new(Move, ActionScope.Entity, "item.manage"), new(Export, ActionScope.Collection, "item.manage")];
        ResourceOperation[] operations = [ResourceOperation.List, ResourceOperation.Get, ResourceOperation.Create, ResourceOperation.Update];
        return new(Id, Version, moduleId, new("id"), reverseDefinitions ? fields.Reverse() : fields, operations,
            new(operations.Select(operation => new OperationSecurityRule(operation, ["item.manage"]))),
            [new("parent", RelationKind.ManyToOne, Id, "parentId", "id", "name")], reverseDefinitions ? actions.Reverse() : actions,
            presentation: includePresentation ? new(form ?? Form()) : null);
    }
    internal static ResourceFormDefinition Form() => new(
    [new("general",
        [new("identity", [new("basic", ["code", "name"], "Identification"), new("classification", ["itemType", "categoryId"], "Classification")], "Identity"),
            new("description", [new("notes", ["description"], "Description"), new("status", ["active"], "Status")], "Details", collapsible: true)], "General"),
        new("logistics",
        [new("dimensions", [new("package", ["weight", "length"], "Package"), new("shape", ["width", "height"], "Shape")], "Dimensions"),
            new("placement", [new("warehouse", ["warehouseId", "locationId"], "Location"), new("tracking", ["batchTracked", "serialTracked"], "Traceability")], "Placement", actions: [Move])], "Logistics"),
        new("inventory",
        [new("levels", [new("thresholds", ["minStock", "maxStock"], "Thresholds"), new("actual", ["quantity", "reorderPoint"], "Balances")], "Stock levels"),
            new("valuation", [new("pricing", ["cost", "currency"], "Valuation"), new("units", ["stockUnit", "stockDate"], "Units")], "Valuation", collapsible: true, initiallyCollapsed: true)], "Inventory", actions: [Recount]),
        new("configuration",
        [new("policy", [new("behavior", ["allowNegative"], "Policy"), new("validity", ["validFrom"], "Validity")], "Policy"),
            new("timing", [new("time", ["cutoff"], "Cutoff"), new("relations", ["parentId"], "Parent item")], "Timing")], "Configuration"),
        new("audit",
        [new("identifiers", [new("primary", ["id"], "Identifier"), new("external", ["externalCode"], "External identity")], "Identifiers"),
            new("timestamps", [new("created", ["createdAt"], "Created"), new("modified", ["updatedAt"], "Modified")], "History", collapsible: true)], "Audit")], actions: [Duplicate]);
}
