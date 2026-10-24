using Alxarafe.Modules.Wms.Domain;
using Xunit;

namespace Alxarafe.ArchitectureTests;

public sealed class InventoryTests
{
    [Theory]
    [InlineData("ALJARAFE-01")]
    [InlineData("A.01.01")]
    [InlineData("AGUA-X-1L")]
    public void NaturalIdentifiersAllowWarehouseLocationAndSkuSyntax(string code) => Assert.True(InventoryRules.ValidCode(code));

    [Theory]
    [InlineData("")]
    [InlineData("lower")]
    [InlineData(" SPACE")]
    [InlineData("A/01")]
    [InlineData("A\n")]
    public void IdentifiersRejectAmbiguousOrUnsafeSyntax(string code) => Assert.False(InventoryRules.ValidCode(code));

    [Fact]
    public void QuantitiesAreSignedBoundedAndExactWithoutRounding()
    {
        Assert.True(InventoryRules.ValidQuantity(0.000001m, InventoryRules.Receipt));
        Assert.True(InventoryRules.ValidQuantity(-0.5m, InventoryRules.Issue));
        Assert.True(InventoryRules.ValidQuantity(-1m, InventoryRules.Adjustment));
        Assert.True(InventoryRules.ValidQuantity(InventoryRules.QuantityMaximum, InventoryRules.Adjustment));
        Assert.False(InventoryRules.ValidQuantity(0, InventoryRules.Receipt));
        Assert.False(InventoryRules.ValidQuantity(-1, InventoryRules.Receipt));
        Assert.False(InventoryRules.ValidQuantity(1, InventoryRules.Issue));
        Assert.False(InventoryRules.ValidQuantity(0.0000001m, InventoryRules.Receipt));
        Assert.False(InventoryRules.ValidQuantity(decimal.MinValue, InventoryRules.Adjustment));
        Assert.False(InventoryRules.ValidQuantity(decimal.MaxValue, InventoryRules.Adjustment));
        Assert.False(InventoryRules.ValidQuantity(1, "UNKNOWN"));
    }

    [Fact]
    public void EntitiesRequireStableReferencesAndNormalizeOnlyLabelsAndTimestampOffsets()
    {
        var id = Guid.NewGuid();
        Assert.Null(Warehouse.Create(Guid.Empty, "WH", "Name"));
        Assert.Null(Warehouse.Create(id, "WH", " "));
        Assert.Equal("Name", Warehouse.Create(id, "WH", " Name ")!.Name);
        Assert.Null(Location.Create(id, Guid.Empty, "A.01", true));
        Assert.Null(StockItem.Create(id, "SKU", "Item", Guid.Empty, null));
        Assert.NotNull(StockItem.Create(id, "SKU", "Item", id, null));
        Assert.Null(StockItem.Create(id, "SKU", "Item", id, Guid.Empty));
        var time = new DateTimeOffset(2026, 6, 20, 9, 0, 0, TimeSpan.FromHours(2));
        var movement = InventoryMovement.Create(id, id, id, id, time, 0.5m, id, InventoryRules.Receipt, "KEY");
        Assert.Equal(time.ToUniversalTime(), movement!.OccurredAt);
        Assert.Equal(TimeSpan.Zero, movement.OccurredAt.Offset);
        Assert.Null(InventoryMovement.Create(id, id, id, id, time.AddTicks(1), 1, id, InventoryRules.Receipt, "KEY"));
    }
}
