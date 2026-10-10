using Alxarafe.Security.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Alxarafe.Host;

public static class AuthEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/register", async (RegisterRequest request, UserManager<AlxarafeUser> userManager,
            SecurityDbContext database, UserAdministration users) =>
        {
            await using var transaction = await database.Database.BeginTransactionAsync();
            await users.LockAsync();
            var normalized = userManager.NormalizeEmail(request.Email);
            if (await database.Users.AnyAsync(user => user.NormalizedEmail == normalized))
            {
                var duplicate = userManager.ErrorDescriber.DuplicateEmail(request.Email);
                return Results.ValidationProblem(new Dictionary<string, string[]> { [duplicate.Code] = [duplicate.Description] });
            }
            var user = new AlxarafeUser { Id = Guid.NewGuid(), UserName = request.Email, Email = request.Email };
            var result = await userManager.CreateAsync(user, request.Password);
            if (result.Succeeded) await transaction.CommitAsync();
            return result.Succeeded
                ? Results.Ok()
                : Results.ValidationProblem(result.Errors.GroupBy(error => error.Code)
                    .ToDictionary(group => group.Key, group => group.Select(error => error.Description).ToArray()));
        }).WithTags("Authentication").WithDescription("Implementation-specific Identity registration, outside ERBAS shared conformance. Portable clients must use administrator-created accounts via POST /api/users; this extension has no shared stability guarantee.");

        LoginEndpoint.Map(endpoints);
    }
}

public sealed record RegisterRequest(string Email, string Password);
