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

        endpoints.MapPost("/api/auth/login", async (LoginRequest request, SignInManager<AlxarafeUser> signInManager) =>
        {
            signInManager.AuthenticationScheme = IdentityConstants.BearerScheme;
            var result = await signInManager.PasswordSignInAsync(request.Email, request.Password, isPersistent: false, lockoutOnFailure: true);
            return result.Succeeded ? Results.Empty : Results.Problem(statusCode: StatusCodes.Status401Unauthorized);
        }).WithTags("Authentication");
    }
}

public sealed record RegisterRequest(string Email, string Password);
public sealed record LoginRequest(string Email, string Password);
