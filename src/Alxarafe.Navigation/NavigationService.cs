using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Alxarafe.Core;
using Alxarafe.Modularity;
using Alxarafe.Resources;
using Alxarafe.Resources.Runtime;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Localization;

namespace Alxarafe.Navigation;

public sealed class NavigationService(NavigationRegistry registry, ResourceRegistry resources, ResourceRepositoryCatalog repositories,
    ModuleStateService modules, IRequestTenantContext tenant, IResourceAuthorization authorization, IStringLocalizerFactory labels,
    IHttpContextAccessor http, ICurrentSecurityContext security)
{
    internal static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web);
    public async Task<NavigationResponse> ResolveAsync(CancellationToken ct = default)
    {
        var principal = http.HttpContext?.User;
        var current = principal is null ? null : await security.ResolveAsync(principal, ct);
        string subject = current?.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();
        var moduleState = new Dictionary<string, bool>(StringComparer.Ordinal);
        async Task<bool> Enabled(string id)
        {
            if (!moduleState.TryGetValue(id, out bool enabled)) moduleState[id] = enabled = await modules.IsEnabledAsync(id, ct);
            return enabled;
        }
        bool contextChecked = false; string context = "platform"; bool hasTenant = false;
        async Task<bool> Tenant()
        {
            if (!contextChecked)
            {
                contextChecked = true;
                try { context = (await tenant.RequireAsync(ct)).Id; hasTenant = true; }
                catch (TenantAccessException failure) when (!failure.ConfigurationFailure) { context = failure.Code; }
            }
            return hasTenant;
        }
        // Even equal platform-only trees are scoped to the established context.
        await Tenant();
        async Task<NavigationItem?> Item(NavigationDefinition definition)
        {
            if (!await Enabled(definition.ModuleId) || (definition.RequiresTenant && !await Tenant())) return null;
            if (definition.Permissions.Count > 0 && !await authorization.IsAllowedAsync(definition.Permissions, ct)) return null;
            if (definition.Target is ResourceNavigationTarget target)
            {
                var resource = resources.Require(target.Resource);
                if (!await Enabled(resource.ModuleId)) return null;
                var binding = repositories.Bindings.Single(item => item.ResourceId == resource.Id);
                if (binding.Scope == ResourceRepositoryScope.Tenant && !await Tenant()) return null;
                if (!await authorization.IsAllowedAsync(resource.Security.Rules.Single(rule => rule.Operation == ResourceOperation.List).Permissions, ct)) return null;
            }
            var children = new List<NavigationItem>();
            foreach (var child in registry.All.Where(item => item.ParentId == definition.Id))
                if (await Item(child) is { } item) children.Add(item);
            if (definition.Target is null && children.Count == 0) return null;
            string label = definition.LabelResource is { } source ? labels.Create(source)[definition.Label].Value : definition.Label;
            return new(definition.Id, label, definition.Order, children.AsReadOnly(), definition.Target, definition.Icon);
        }
        var roots = new List<NavigationItem>();
        foreach (var root in registry.All.Where(item => item.ParentId is null))
            if (await Item(root) is { } item) roots.Add(item);
        byte[] fingerprint = JsonSerializer.SerializeToUtf8Bytes(new { subject, context, hasTenant, culture = CultureInfo.CurrentUICulture.Name, items = roots }, Json);
        return new(Convert.ToHexStringLower(SHA256.HashData(fingerprint)), roots.AsReadOnly());
    }
}
