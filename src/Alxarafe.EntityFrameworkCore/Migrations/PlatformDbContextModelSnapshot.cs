using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Alxarafe.EntityFrameworkCore.Migrations;

[DbContext(typeof(PlatformDbContext))]
public sealed class PlatformDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder) => AuthoritativeTenancy.ConfigureFrozenModel(modelBuilder);
}
