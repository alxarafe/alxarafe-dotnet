using Alxarafe.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Alxarafe.Modules.Wms.Http;

public static class InventoryPermissions
{
    public const string WarehousesRead = "wms.warehouses.read";
    public const string WarehousesWrite = "wms.warehouses.write";
    public const string LocationsRead = "wms.locations.read";
    public const string LocationsWrite = "wms.locations.write";
    public const string ItemsRead = "wms.items.read";
    public const string ItemsWrite = "wms.items.write";
    public const string MovementsRead = "wms.movements.read";
    public const string MovementsWrite = "wms.movements.write";
    public const string InventoryRead = "wms.inventory.read";
    public static readonly IReadOnlyList<string> All = Array.AsReadOnly(new[] { WarehousesRead, WarehousesWrite,
        LocationsRead, LocationsWrite, ItemsRead, ItemsWrite, MovementsRead, MovementsWrite, InventoryRead });

    public static void Configure(IServiceCollection services) => services.AddAuthorization(options =>
    {
        foreach (string permission in All)
            options.AddPolicy(permission, policy => policy.RequireAuthenticatedUser().RequireAssertion(context =>
                context.User.HasClaim(CoreUserClaims.Admin, "true") || context.User.HasClaim(PermissionClaim.Type, permission)));
    });
}
