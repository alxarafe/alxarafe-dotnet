using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Alxarafe.Modules.Wms.Infrastructure.Migrations;

[DbContext(typeof(WmsDbContext))]
public sealed class WmsDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder) => ItemFamilies.BuildFrozenModel(modelBuilder);
}
