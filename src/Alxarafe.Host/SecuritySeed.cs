using System.Security.Claims;
using Alxarafe.Security;
using Alxarafe.Security.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Alxarafe.Host;

public static class SecuritySeed
{
    public static async Task InitializeAsync(IServiceProvider services, IHostEnvironment environment)
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing")) return;

        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        await provider.GetRequiredService<SecurityDbContext>().Database.EnsureCreatedAsync();
        var userManager = provider.GetRequiredService<UserManager<AlxarafeUser>>();

        foreach (var user in provider.GetRequiredService<IOptions<DevelopmentUsersOptions>>().Value.Users)
            await EnsureUserAsync(userManager, user.Email, user.Password, user.Permissions);
    }

    private static async Task EnsureUserAsync(UserManager<AlxarafeUser> userManager, string email, string password, IReadOnlyCollection<string> permissions)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new AlxarafeUser { Id = Guid.NewGuid(), UserName = email, Email = email, EmailConfirmed = true };
            var result = await userManager.CreateAsync(user, password);
            if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Errors.Select(error => error.Description)));
        }

        var existing = await userManager.GetClaimsAsync(user);
        foreach (var permission in permissions.Where(permission => !existing.Any(claim => claim.Type == PermissionClaim.Type && claim.Value == permission)))
            await userManager.AddClaimAsync(user, new Claim(PermissionClaim.Type, permission));
    }
}
