using Alxarafe.Modules.Catalog.Domain;
using Xunit;

namespace Alxarafe.Modules.Catalog.Domain.Tests;

public sealed class ItemTests
{
    [Fact]
    public void CreateNormalizesSkuAndName()
    {
        var item = Item.Create(Sku.Create(" sku-1 "), ItemName.Create("  Desk  "));
        Assert.Equal("SKU-1", item.Sku.Value);
        Assert.Equal("Desk", item.Name.Value);
    }

    [Fact]
    public void CreateRejectsEmptySku()
    {
        Assert.Throws<ArgumentException>(() => Sku.Create(" "));
    }
}
