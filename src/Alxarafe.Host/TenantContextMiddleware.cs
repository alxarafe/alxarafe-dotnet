using Alxarafe.Core;
using Alxarafe.Security;

namespace Alxarafe.Host;

internal sealed class TenantContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IRequestTenantContext tenant)
    {
        // CurrentUserMiddleware has already validated the persisted protected-resource user.
        // Anonymous liveness never resolves security, memberships or business persistence.
        if (context.Items.ContainsKey(typeof(CoreUserState)))
        {
            try { await tenant.RequireAsync(context.RequestAborted); }
            catch (TenantAccessException)
            {
                // Control-plane operations need no tenant. Business persistence requires
                // a successful fresh resolution and propagates an explicit denial.
            }
        }
        await next(context);
    }
}
