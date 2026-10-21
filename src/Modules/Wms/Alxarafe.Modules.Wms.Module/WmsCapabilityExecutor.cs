using Alxarafe.AspNetCore;
using Alxarafe.Modularity;
using Alxarafe.Modules.Wms.Application;
using Alxarafe.Modules.Wms.Http;

namespace Alxarafe.Modules.Wms;

internal sealed class WmsCapabilityExecutor(UomCatalogue uoms, StorageAttributeCatalogue storageAttributes,
    ItemFamilyCatalogue itemFamilies) : ICapabilityExecutor
{
    public bool CanExecute(string capabilityId) => WmsCapabilities.All.Any(capability => capability.Id == capabilityId);

    public async Task<object?> ExecuteAsync(CapabilityDescriptor capability, CapabilityListInput input,
        CapabilityExecutionContext context, CancellationToken cancellationToken = default)
    {
        var pagination = new CollectionPagination(input.Offset, input.Limit);
        switch (capability.Id)
        {
            case WmsCapabilities.UomsList:
                return UomEndpoints.ToPage(await uoms.ListAsync(pagination.Offset, pagination.Limit, cancellationToken), pagination);
            case WmsCapabilities.StorageAttributesList:
                return StorageAttributeEndpoints.ToPage(
                    await storageAttributes.ListAsync(pagination.Offset, pagination.Limit, cancellationToken), pagination);
            case WmsCapabilities.ItemFamiliesList:
                return ItemFamilyEndpoints.ToPage(
                    await itemFamilies.ListAsync(pagination.Offset, pagination.Limit, cancellationToken), pagination);
            default:
                throw new InvalidOperationException("Unsupported capability execution.");
        }
    }
}
