using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Alxarafe.Modules.Assets.Migrations;

[DbContext(typeof(AssetsDbContext))]
public sealed class AssetsDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder) => InitialAssets.ConfigureModel(modelBuilder);
}
