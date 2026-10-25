using Alxarafe.Core;
using Alxarafe.Modularity;

namespace Alxarafe.Resources.Runtime;

public sealed class ResourceActionRuntime(ResourceRegistry resources, ModuleStateService modules, ResourceRepositoryCatalog scopes,
    ResourceActionRegistry actions, IRequestTenantContext tenant, IResourceAuthorization authorization)
{
    public const int MaximumSelection = ResourceQuery.MaximumLimit;
    public async Task ExecuteAsync(string resourceId, string actionName, ResourceActionTarget target, ResourceActionInput input,
        CancellationToken cancellationToken = default) => await Safe(resourceId, async () =>
    {
        var definition = Definition(resourceId);
        var action = definition.Actions.SingleOrDefault(action => action.Name == actionName)
            ?? throw new ResourceRuntimeException(resourceId, ResourceRuntimeError.ActionNotFound);
        await Guard(definition, cancellationToken);
        if (!await authorization.IsAllowedAsync([action.Permission], cancellationToken)) throw new ResourceRuntimeException(resourceId, ResourceRuntimeError.Forbidden);
        Target(definition, target, action.Scope);
        var values = Input(definition, action, input);
        var context = new ResourceActionContext(definition, action, target);
        var availability = await actions.AvailabilityAsync(context, cancellationToken);
        if (!availability.Available) throw new ResourceRuntimeException(resourceId, ResourceRuntimeError.ActionUnavailable, reasonCode: availability.ReasonCode);
        var handler = actions.Require(resourceId, actionName);
        ResourceActionOutcome result;
        try { result = await handler.ExecuteAsync(new(context, values), cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { throw new ResourceRuntimeException(resourceId, ResourceRuntimeError.ExecutionFailed); }
        if (result is null || !Enum.IsDefined(result.Kind) || (result.Kind != ResourceActionOutcomeKind.Conflict && result.ReasonCode is not null)
            || (result.Kind == ResourceActionOutcomeKind.Conflict && !ResourceActionReason.Valid(result.ReasonCode))) throw new InvalidOperationException("Invalid action outcome.");
        if (result.Kind == ResourceActionOutcomeKind.NotFound) throw new ResourceRuntimeException(resourceId, ResourceRuntimeError.ResourceNotFound);
        if (result.Kind == ResourceActionOutcomeKind.Conflict) throw new ResourceRuntimeException(resourceId, ResourceRuntimeError.ActionConflict, reasonCode: result.ReasonCode);
        return true;
    }, cancellationToken);

    public Task<IReadOnlyList<ResourceActionState>> AvailabilityAsync(string resourceId, ResourceActionTarget target,
        CancellationToken cancellationToken = default) => Safe<IReadOnlyList<ResourceActionState>>(resourceId, async () =>
    {
        var definition = Definition(resourceId);
        await Guard(definition, cancellationToken);
        var scope = Scope(resourceId, target);
        var states = new List<ResourceActionState>();
        foreach (var action in definition.Actions.Where(action => action.Scope == scope).OrderBy(action => action.Name, StringComparer.Ordinal))
        {
            if (!await authorization.IsAllowedAsync([action.Permission], cancellationToken)) { states.Add(new(action.Name, false, false)); continue; }
            Target(definition, target, scope);
            _ = actions.Require(resourceId, action.Name);
            var available = await actions.AvailabilityAsync(new(definition, action, target), cancellationToken);
            states.Add(new(action.Name, true, available.Available, available.ReasonCode));
        }
        return Array.AsReadOnly(states.ToArray());
    }, cancellationToken);

    private ResourceDefinition Definition(string id) => resources.TryGet(id, out var definition) ? definition! : throw new ResourceRuntimeException(id, ResourceRuntimeError.ResourceNotFound);
    private async Task Guard(ResourceDefinition definition, CancellationToken ct)
    {
        if (!await modules.IsEnabledAsync(definition.ModuleId, ct)) throw new ResourceRuntimeException(definition.Id, ResourceRuntimeError.ModuleDisabled);
        ResourceRepositoryScope scope;
        try { scope = scopes.Scope(definition); }
        catch (ResourceRuntimeException) { throw new ResourceRuntimeException(definition.Id, ResourceRuntimeError.ActionHandlerNotRegistered); }
        if (scope == ResourceRepositoryScope.Tenant) await tenant.RequireAsync(ct);
    }
    private static ActionScope Scope(string resourceId, ResourceActionTarget target) => target switch
    {
        ResourceEntityTarget => ActionScope.Entity,
        ResourceCollectionTarget => ActionScope.Collection,
        ResourceSelectionTarget => ActionScope.Selection,
        _ => throw new ResourceRuntimeException(resourceId, ResourceRuntimeError.ValidationFailed, [new("target", ResourceInputError.InvalidValue, "Invalid action target.")])
    };
    private static void Target(ResourceDefinition definition, ResourceActionTarget target, ActionScope scope)
    {
        if (target is null || Scope(definition.Id, target) != scope) Reject(definition.Id, "target");
        switch (target)
        {
            case ResourceEntityTarget entity: RuntimeValidation.Identifier(definition, entity.Identifier); break;
            case ResourceSelectionTarget selection:
                if (selection.Identifiers.Count is 0 or > MaximumSelection) Reject(definition.Id, "target.ids");
                var seen = new HashSet<ResourceIdentifier>();
                foreach (var id in selection.Identifiers) { RuntimeValidation.Identifier(definition, id); if (!seen.Add(id)) Reject(definition.Id, "target.ids"); }
                break;
        }
    }
    private static ResourceActionInput Input(ResourceDefinition definition, ActionDefinition action, ResourceActionInput input)
    {
        if (input is null) Reject(definition.Id, "parameters");
        var values = new SortedDictionary<string, ScalarValue>(StringComparer.Ordinal);
        foreach (var pair in input!.Values)
        {
            var parameter = action.Parameters.SingleOrDefault(parameter => parameter.Name == pair.Key);
            if (parameter is null || !ResourceValueValidator.IsValid(parameter, pair.Value)) Reject(definition.Id, "parameters." + pair.Key);
            values.Add(pair.Key, pair.Value);
        }
        foreach (var parameter in action.Parameters)
        {
            if (!values.ContainsKey(parameter.Name) && parameter.DefaultValue is { } value)
            {
                if (!ResourceValueValidator.IsValid(parameter, value)) Reject(definition.Id, "parameters." + parameter.Name);
                values.Add(parameter.Name, value);
            }
            if (parameter.Required && !values.ContainsKey(parameter.Name)) Reject(definition.Id, "parameters." + parameter.Name);
        }
        return new(values);
    }
    private static void Reject(string resourceId, string path) => throw new ResourceRuntimeException(resourceId, ResourceRuntimeError.ValidationFailed,
        [new(path, ResourceInputError.InvalidValue, "Invalid action target or parameters.")]);
    private static async Task<T> Safe<T>(string id, Func<Task<T>> operation, CancellationToken ct)
    {
        try { ct.ThrowIfCancellationRequested(); return await operation(); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw new OperationCanceledException(ct); }
        catch (ResourceRuntimeException) { throw; }
        catch (TenantAccessException failure) when (!failure.ConfigurationFailure) { throw new ResourceRuntimeException(id, ResourceRuntimeError.TenantRequired); }
        catch (Exception) { throw new ResourceRuntimeException(id, ResourceRuntimeError.ExecutionFailed); }
    }
}
