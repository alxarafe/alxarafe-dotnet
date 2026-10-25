namespace Alxarafe.Resources.Runtime;

// Like repository composition, scoped adapters are snapshotted and validated eagerly
// at startup. Metadata-only actions remain valid but execution fails closed.
public sealed class ResourceActionRegistry
{
    private readonly Dictionary<(string Resource, string Action), IResourceActionHandler> _handlers = new();
    private readonly Dictionary<(string Resource, string Action), IResourceActionAvailabilityProvider> _providers = new();
    public ResourceActionRegistry(ResourceRegistry resources, ResourceRepositoryCatalog scopes,
        IEnumerable<IResourceActionHandler> handlers, IEnumerable<IResourceActionAvailabilityProvider> providers)
    {
        foreach (var handler in handlers)
        {
            if (handler is null) throw new InvalidOperationException("Null resource action handler.");
            var identity = (handler.ResourceId, handler.ActionName);
            Validate(identity.ResourceId, identity.ActionName);
            if (!_handlers.TryAdd(identity, handler)) throw new InvalidOperationException("Duplicate resource action handler.");
        }
        foreach (var provider in providers)
        {
            if (provider is null) throw new InvalidOperationException("Null resource action availability provider.");
            var identity = (provider.ResourceId, provider.ActionName);
            Validate(identity.ResourceId, identity.ActionName);
            if (!_providers.TryAdd(identity, provider)) throw new InvalidOperationException("Duplicate resource action availability provider.");
        }
        void Validate(string resourceId, string actionName)
        {
            if (string.IsNullOrWhiteSpace(resourceId) || string.IsNullOrWhiteSpace(actionName) || !resources.TryGet(resourceId, out var resource) || !resource!.Actions.Any(action => action.Name == actionName))
                throw new InvalidOperationException("Unknown resource or action in action composition.");
            _ = scopes.Scope(resource);
        }
    }
    internal IResourceActionHandler Require(string resourceId, string actionName)
    {
        if (!_handlers.TryGetValue((resourceId, actionName), out var handler))
            throw new ResourceRuntimeException(resourceId, ResourceRuntimeError.ActionHandlerNotRegistered);
        if (handler.ResourceId != resourceId || handler.ActionName != actionName) throw new InvalidOperationException("Action handler ownership changed.");
        return handler;
    }
    internal async Task<ResourceActionAvailability> AvailabilityAsync(ResourceActionContext context, CancellationToken ct)
    {
        if (!_providers.TryGetValue((context.Definition.Id, context.Action.Name), out var provider)) return new(true);
        if (provider.ResourceId != context.Definition.Id || provider.ActionName != context.Action.Name) throw new InvalidOperationException("Availability ownership changed.");
        ResourceActionAvailability result;
        try { result = await provider.EvaluateAsync(context, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { throw new ResourceRuntimeException(context.Definition.Id, ResourceRuntimeError.ExecutionFailed); }
        if (result is null || (result.Available && result.ReasonCode is not null) || (result.ReasonCode is not null && !ResourceActionReason.Valid(result.ReasonCode)))
            throw new InvalidOperationException("Invalid action availability result.");
        return result;
    }
}
