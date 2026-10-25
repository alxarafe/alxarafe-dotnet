using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Alxarafe.Modules.Ai.Persistence.Migrations;

[DbContext(typeof(AiUsageDbContext))]
public sealed class AiUsageDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.0");
        TenantUsageAttribution.ConfigureModel(modelBuilder);
    }
}
