using Alxarafe.Core;
using Alxarafe.Modularity;

namespace Alxarafe.Resources.Runtime;

public sealed class ResourceRuntime(ResourceRegistry resources, ModuleStateService modules, ResourceRepositoryCatalog catalog,
    ResourceRepositoryRegistry repositories, IRequestTenantContext tenant, IResourceAuthorization authorization)
{
    public Task<ResourcePage> ListAsync(string resourceId, ResourceQuery? query = null, CancellationToken cancellationToken = default) =>
        ExecuteAsync(resourceId, ResourceOperation.List, async (definition, ct) =>
        {
            query ??= new();
            RuntimeValidation.Query(definition, query);
            var page = await repositories.Require(resourceId).ListAsync(query, ct);
            RuntimeValidation.Page(definition, page, query);
            return page;
        }, cancellationToken);

    public Task<ResourceRecord> GetAsync(string resourceId, ResourceIdentifier identifier, CancellationToken cancellationToken = default) =>
        ExecuteAsync(resourceId, ResourceOperation.Get, async (definition, ct) =>
        {
            RuntimeValidation.Identifier(definition, identifier);
            var record = await repositories.Require(resourceId).GetAsync(identifier, ct)
                ?? throw new ResourceRuntimeException(resourceId, ResourceRuntimeError.ResourceNotFound);
            RuntimeValidation.Complete(definition, record, identifier);
            return record;
        }, cancellationToken);

    public Task<ResourceRecord> CreateAsync(string resourceId, ResourceRecord values, CancellationToken cancellationToken = default) =>
        ExecuteAsync(resourceId, ResourceOperation.Create, async (definition, ct) =>
        {
            var input = RuntimeValidation.Input(definition, values, create: true);
            var repository = repositories.Require(resourceId);
            await repositories.ValidateAsync(new(definition, ResourceOperation.Create, null, null, input), ct);
            var result = await repository.CreateAsync(input, ct);
            RuntimeValidation.Complete(definition, result);
            return result;
        }, cancellationToken);

    public Task<ResourceRecord> UpdateAsync(string resourceId, ResourceIdentifier identifier, ResourceRecord patch, CancellationToken cancellationToken = default) =>
        ExecuteAsync(resourceId, ResourceOperation.Update, async (definition, ct) =>
        {
            RuntimeValidation.Identifier(definition, identifier);
            var input = RuntimeValidation.Input(definition, patch, create: false);
            var repository = repositories.Require(resourceId);
            var current = await repository.GetAsync(identifier, ct) ?? throw new ResourceRuntimeException(resourceId, ResourceRuntimeError.ResourceNotFound);
            RuntimeValidation.Complete(definition, current, identifier);
            var merged = RuntimeValidation.Merge(current, input);
            RuntimeValidation.Complete(definition, merged, identifier);
            await repositories.ValidateAsync(new(definition, ResourceOperation.Update, identifier, current, merged), ct);
            // Persist only supplied fields. Adapter owns atomic patch/invariant enforcement.
            var result = await repository.UpdateAsync(identifier, input, ct) ?? throw new ResourceRuntimeException(resourceId, ResourceRuntimeError.ResourceNotFound);
            RuntimeValidation.Complete(definition, result, identifier);
            return result;
        }, cancellationToken);

    public async Task DeleteAsync(string resourceId, ResourceIdentifier identifier, CancellationToken cancellationToken = default) =>
        await ExecuteAsync(resourceId, ResourceOperation.Delete, async (definition, ct) =>
        {
            RuntimeValidation.Identifier(definition, identifier);
            var repository = repositories.Require(resourceId);
            await repositories.ValidateAsync(new(definition, ResourceOperation.Delete, identifier, null, null), ct);
            if (!await repository.DeleteAsync(identifier, ct)) throw new ResourceRuntimeException(resourceId, ResourceRuntimeError.ResourceNotFound);
            return true;
        }, cancellationToken);

    private async Task<T> ExecuteAsync<T>(string resourceId, ResourceOperation operation,
        Func<ResourceDefinition, CancellationToken, Task<T>> execute, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!resources.TryGet(resourceId, out var definition)) throw new ResourceRuntimeException(resourceId, ResourceRuntimeError.ResourceNotFound);
            if (!definition!.Operations.Contains(operation)) throw new ResourceRuntimeException(resourceId, ResourceRuntimeError.OperationNotSupported);
            if (!await modules.IsEnabledAsync(definition.ModuleId, cancellationToken)) throw new ResourceRuntimeException(resourceId, ResourceRuntimeError.ModuleDisabled);
            if (catalog.Scope(definition) == ResourceRepositoryScope.Tenant) await tenant.RequireAsync(cancellationToken);
            var permissions = definition.Security.Rules.Single(rule => rule.Operation == operation).Permissions;
            if (!await authorization.IsAllowedAsync(permissions, cancellationToken)) throw new ResourceRuntimeException(resourceId, ResourceRuntimeError.Forbidden);
            return await execute(definition, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
        catch (ResourceRuntimeException) { throw; }
        catch (ResourceConflictException conflict)
        { throw new ResourceRuntimeException(resourceId, ResourceRuntimeError.Conflict, conflict.Issues, conflict.ReasonCode); }
        catch (TenantAccessException failure) when (!failure.ConfigurationFailure)
        { throw new ResourceRuntimeException(resourceId, ResourceRuntimeError.TenantRequired); }
        catch (Exception)
        {
            // Adapter messages/inner exceptions can contain SQL or credentials. Never
            // surface them through the neutral execution contract.
            throw new ResourceRuntimeException(resourceId, ResourceRuntimeError.ExecutionFailed);
        }
    }
}
