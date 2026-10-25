using Alxarafe.Core;
using Microsoft.EntityFrameworkCore;

namespace Alxarafe.Modules.Wms.Infrastructure;

public sealed class TenantWmsDbContextFactory(ITenantDatabaseResolver databases) : IDbContextFactory<WmsDbContext>
{
    public WmsDbContext CreateDbContext() => throw new InvalidOperationException("Tenant persistence requires asynchronous context creation.");

    public async Task<WmsDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>();
        WmsStorage.Configure(options, await databases.ResolveAsync(cancellationToken), testing: false);
        return new WmsDbContext(options.Options);
    }
}
