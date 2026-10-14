using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Alxarafe.AspNetCore;
using Alxarafe.Security;
using Alxarafe.Security.AspNetCore;
using Alxarafe.Security.EntityFrameworkCore;
using Microsoft.OpenApi;

namespace Alxarafe.Host;

public static class UserEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/auth/me", (HttpContext context) => Json(200, User.From((CoreUserState)context.Items[typeof(CoreUserState)]!)))
            .RequireAuthorization().WithSharedContractErrors().WithTags("Authentication").Produces<User>().Produces<UserError>(401);
        var group = endpoints.MapGroup("/api/users").RequireAuthorization(CoreAdministration.Policy).WithTags("Users").WithSharedContractErrors();
        group.MapGet("", ListAsync).Produces<UserPage>().Produces<UserError>(400).Produces<UserError>(401).Produces<UserError>(403)
            .AddOpenApiOperationTransformer((operation, _, _) =>
            {
                operation.Parameters =
                [
                    new OpenApiParameter { Name = CollectionPagination.OffsetParameter, In = ParameterLocation.Query,
                        Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Minimum = "0", Default = JsonValue.Create(0) } },
                    new OpenApiParameter { Name = CollectionPagination.LimitParameter, In = ParameterLocation.Query,
                        Schema = new OpenApiSchema { Type = JsonSchemaType.Integer,
                            Minimum = CollectionPagination.MinimumLimit.ToString(CultureInfo.InvariantCulture),
                            Maximum = CollectionPagination.MaximumLimit.ToString(CultureInfo.InvariantCulture),
                            Default = JsonValue.Create(CollectionPagination.DefaultLimit) } }
                ];
                return Task.CompletedTask;
            });
        group.MapGet("/{id}", async (string id, UserAdministration users) =>
        {
            var user = Guid.TryParse(id, out var key) ? await users.FindAsync(key) : null;
            return user is null ? Error(404, "user_not_found") : Json(200, User.From(user));
        }).Produces<User>().Produces<UserError>(401).Produces<UserError>(403).Produces<UserError>(404);
        group.MapPost("", CreateAsync).Accepts<CreateUserRequest>("application/json").Produces<User>(201)
            .Produces<UserError>(400).Produces<UserError>(401).Produces<UserError>(403).Produces<UserError>(409);
        group.MapPatch("/{id}", UpdateAsync).Accepts<UpdateUserRequest>("application/json").Produces<User>()
            .Produces<UserError>(400).Produces<UserError>(401).Produces<UserError>(403).Produces<UserError>(404).Produces<UserError>(409);
    }

    private static async Task<IResult> ListAsync(HttpContext context, UserAdministration users)
    {
        var pagination = CollectionPagination.Read(context.Request.Query);
        if (pagination is null)
            return Error(400, "invalid_request");
        var (offset, limit) = pagination;
        long total = await users.CountAsync();
        var items = offset >= total ? [] : await users.ListAsync((long)offset, (int)limit);
        return Json(200, new UserPage(items.Select(User.From).ToArray(), offset, limit, total, CollectionPagination.IdAscending));
    }

    private static async Task<IResult> CreateAsync(HttpContext context, UserAdministration users)
    {
        using var document = await ReadAsync(context);
        if (document is null || !Fields(document.RootElement, ["email", "password", "admin"], 3)
            || !String(document.RootElement, "email", out var email) || email.Length == 0
            || !String(document.RootElement, "password", out var password)
            || !Boolean(document.RootElement, "admin", out var admin)) return Error(400, "invalid_request");
        int length = password.EnumerateRunes().Count();
        if (length < 12 || length > 256) return Error(400, "invalid_request");
        var user = await users.CreateAsync(email, password, admin!.Value);
        return user is null ? Error(409, "email_conflict") : Json(201, User.From(user));
    }

    private static async Task<IResult> UpdateAsync(string id, HttpContext context, UserAdministration users)
    {
        using var document = await ReadAsync(context);
        if (document is null || !Fields(document.RootElement, ["enabled", "admin"], 1)
            || !Boolean(document.RootElement, "enabled", out var enabled) || !Boolean(document.RootElement, "admin", out var admin))
            return Error(400, "invalid_request");
        var result = Guid.TryParse(id, out var key) ? await users.UpdateAsync(key, enabled, admin) : new(UserUpdateOutcome.NotFound);
        return result.Outcome switch
        {
            UserUpdateOutcome.Updated => Json(200, User.From(result.User!)),
            UserUpdateOutcome.LastAdmin => Error(409, "last_admin"),
            _ => Error(404, "user_not_found")
        };
    }

    private static async Task<JsonDocument?> ReadAsync(HttpContext context)
    {
        try { return await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted); }
        catch (JsonException) { return null; }
        catch (BadHttpRequestException) { return null; }
    }

    private static bool Fields(JsonElement root, string[] allowed, int minimum)
    {
        if (root.ValueKind != JsonValueKind.Object) return false;
        var names = root.EnumerateObject().Select(property => property.Name).ToArray();
        return names.Length >= minimum && names.Length <= allowed.Length && names.Distinct(StringComparer.Ordinal).Count() == names.Length
            && names.All(name => allowed.Contains(name, StringComparer.Ordinal));
    }

    private static bool String(JsonElement root, string field, out string value)
    {
        value = "";
        if (!root.TryGetProperty(field, out var property) || property.ValueKind != JsonValueKind.String) return false;
        value = property.GetString()!;
        return true;
    }

    private static bool Boolean(JsonElement root, string field, out bool? value)
    {
        value = null;
        if (!root.TryGetProperty(field, out var property)) return true;
        if (property.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
        value = property.GetBoolean();
        return true;
    }

    internal static IResult Error(int status, string code) => Json(status, new UserError(code));
    private static IResult Json(int status, object body) => Results.Json(body, statusCode: status);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record User([property: Required, MinLength(1)] string Id, [property: Required, MinLength(1)] string Email, bool Enabled, bool Admin)
{
    internal static User From(CoreUserState state) => new(state.Id.ToString(), state.Email, state.Enabled, state.Admin);
}
public sealed record UserError(string Code);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateUserRequest([property: Required, MinLength(1)] string Email,
    [property: Required, MinLength(12), MaxLength(256)] string Password, bool Admin);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateUserRequest(bool? Enabled, bool? Admin);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UserPage(User[] Items, [property: JsonConverter(typeof(OffsetJsonConverter))] BigInteger Offset,
    int Limit, long Total, IReadOnlyList<CollectionOrder> Order);
