using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Alxarafe.Security.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Alxarafe.Host;

internal static class LoginEndpoint
{
    // Endpoint-local options: do not change binding for registration or modules.
    private static readonly JsonSerializerOptions RequestJson = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/api/auth/login", LoginAsync)
            .AllowAnonymous()
            .WithTags("Authentication")
            .Accepts<LoginRequest>("application/json")
            .Produces<LoginResponse>()
            .Produces<LoginError>(StatusCodes.Status400BadRequest)
            .Produces<LoginError>(StatusCodes.Status401Unauthorized);

    private static async Task<IResult> LoginAsync(HttpContext context,
        SignInManager<AlxarafeUser> signInManager, IOptionsMonitor<BearerTokenOptions> bearerOptions)
    {
        LoginRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync<LoginRequest>(RequestJson, context.RequestAborted);
        }
        catch (JsonException)
        {
            return InvalidRequest();
        }
        catch (BadHttpRequestException)
        {
            return InvalidRequest();
        }

        if (request is null || string.IsNullOrEmpty(request.Email) || string.IsNullOrEmpty(request.Password))
            return InvalidRequest();

        signInManager.AuthenticationScheme = IdentityConstants.BearerScheme;
        var user = await signInManager.UserManager.FindByEmailAsync(request.Email);
        if (user is null ||
            !(await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true)).Succeeded ||
            await signInManager.IsTwoFactorEnabledAsync(user))
        {
            // Password-only login must never bypass Identity's second-factor requirement.
            context.Response.Headers.WWWAuthenticate = "Bearer";
            return Results.Json(new LoginError("invalid_credentials"), statusCode: StatusCodes.Status401Unauthorized);
        }

        var principal = await signInManager.CreateUserPrincipalAsync(user);
        principal.Identities.First().AddClaim(new Claim("amr", "pwd"));
        var options = bearerOptions.Get(IdentityConstants.BearerScheme);
        var properties = new AuthenticationProperties
        {
            ExpiresUtc = (options.TimeProvider ?? TimeProvider.System).GetUtcNow() + options.BearerTokenExpiration
        };
        // Use the configured scheme's public protector, not the handler's automatic
        // sign-in response (which also emits expiry and refresh-token fields).
        var ticket = new AuthenticationTicket(principal, properties, IdentityConstants.BearerScheme);
        var accessToken = options.BearerTokenProtector.Protect(ticket);
        context.Response.Headers.CacheControl = "no-store";
        return Results.Json(new LoginResponse(accessToken));
    }

    private static IResult InvalidRequest() =>
        Results.Json(new LoginError("invalid_request"), statusCode: StatusCodes.Status400BadRequest);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LoginRequest(
    [property: JsonPropertyName("email"), Required, MinLength(1)] string Email,
    [property: JsonPropertyName("password"), Required, MinLength(1)] string Password);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LoginResponse([property: Required, MinLength(1)] string AccessToken);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LoginError([property: Required] string Code);
