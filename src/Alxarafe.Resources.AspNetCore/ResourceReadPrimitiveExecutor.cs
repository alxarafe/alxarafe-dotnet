using System.Text.Json;
using Alxarafe.Modularity;
using Alxarafe.Resources.Runtime;

namespace Alxarafe.Resources.AspNetCore;

public sealed class ResourceReadPrimitiveExecutor(ResourceRegistry resources, ReadPrimitiveRegistry primitives, ResourceRuntime runtime) : IReadPrimitiveExecutor
{
    private ResourceDefinition? Resource(string id) => resources.All.SingleOrDefault(resource =>
        ResourceReadPrimitives.Id(resource.Id) == id && primitives.Primitives.Any(primitive => primitive.Id == id && primitive.Module == resource.ModuleId));
    public bool CanExecute(string primitiveId) => Resource(primitiveId) is not null;
    public async Task<object?> ExecuteAsync(ReadPrimitiveDescriptor primitive, JsonElement input, ReadPrimitiveContext context, CancellationToken ct)
    {
        var resource = Resource(primitive.Id) ?? throw new InvalidOperationException("Resource was not opted into READ primitives.");
        ResourceQuery query;
        try { query = ResourceReadPrimitives.Query(resource, input); }
        catch (Exception failure) when (failure is ResourceTransportException or JsonException or InvalidOperationException or FormatException or ArgumentException)
        { throw new ReadPrimitiveInputException(); }
        // No repository/service principal/tenant substitution. Runtime rechecks
        // the current request's module, persisted authorization and tenant.
        var page = await runtime.ListAsync(resource.Id, query, ct);
        return new ResourcePageDto(page.Items.Select(ResourceTransport.Record).ToArray(), page.Offset, page.Limit, page.Total);
    }
}
