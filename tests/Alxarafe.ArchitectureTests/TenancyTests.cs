using Alxarafe.Core;
using Alxarafe.EntityFrameworkCore;
using Alxarafe.Host;
using Alxarafe.Modules.Wms.Infrastructure;
using Xunit;

namespace Alxarafe.ArchitectureTests;

public sealed class TenancyTests
{
    [Fact]
    public void CanonicalIdentityBelongsToCoreAndRegistryPersistenceToPlatform()
    {
        Assert.Equal(typeof(Alxarafe.Core.AssemblyMarker).Assembly, typeof(Tenant).Assembly);
        Assert.Equal(typeof(PlatformDbContext).Assembly, typeof(TenantRow).Assembly);
        Assert.Equal(typeof(PlatformDbContext).Assembly, typeof(TenantMembershipRow).Assembly);
        Assert.Contains(typeof(IRequestTenantContext), typeof(TenantContext).GetInterfaces());
        Assert.Contains(typeof(ICurrentTenantContext), typeof(TenantContext).GetInterfaces());
    }

    [Fact]
    public void BusinessContextFactoryDependsOnlyOnTheNeutralDatabaseResolver()
    {
        var constructor = Assert.Single(typeof(TenantWmsDbContextFactory).GetConstructors());
        Assert.Equal(typeof(ITenantDatabaseResolver), Assert.Single(constructor.GetParameters()).ParameterType);
    }
}
