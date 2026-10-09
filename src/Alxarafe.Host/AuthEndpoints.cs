using Alxarafe.Security.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;

namespace Alxarafe.Host;

public static class AuthEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/register", async (RegisterRequest request, UserManager<AlxarafeUser> userManager) =>
        {
            var user = new AlxarafeUser { Id = Guid.NewGuid(), UserName = request.Email, Email = request.Email };
            var result = await userManager.CreateAsync(user, request.Password);
            return result.Succeeded
                ? Results.Ok()
                : Results.ValidationProblem(result.Errors.GroupBy(error => error.Code)
                    .ToDictionary(group => group.Key, group => group.Select(error => error.Description).ToArray()));
        }).WithTags("Authentication");

        LoginEndpoint.Map(endpoints);
    }
}

public sealed record RegisterRequest(string Email, string Password);
