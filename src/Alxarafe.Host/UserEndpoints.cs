using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Alxarafe.Security;
using Alxarafe.Security.EntityFrameworkCore;
using Microsoft.OpenApi;

namespace Alxarafe.Host;

public static class UserEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/auth/me", (HttpContext context) => Json(200, User.From((CoreUserState)context.Items[typeof(CoreUserState)]!)))
            .RequireAuthorization().WithTags("Authentication").Produces<User>().Produces<UserError>(401);
        var group = endpoints.MapGroup("/api/users").RequireAuthorization(SharedUsersEndpoint.AdminPolicy).WithTags("Users");
        group.MapGet("", ListAsync).Produces<UserPage>().Produces<UserError>(400).Produces<UserError>(401).Produces<UserError>(403)
            .AddOpenApiOperationTransformer((operation, _, _) =>
            {
                operation.Parameters =
                [
                    new OpenApiParameter { Name = "offset", In = ParameterLocation.Query,
                        Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Minimum = "0", Default = JsonValue.Create(0) } },
                    new OpenApiParameter { Name = "limit", In = ParameterLocation.Query,
                        Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Minimum = "1", Maximum = "100", Default = JsonValue.Create(50) } }
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
        var query = context.Request.Query;
        var offsetText = query.ContainsKey("offset") ? query["offset"].ToString() : "0";
        var limitText = query.ContainsKey("limit") ? query["limit"].ToString() : "50";
        if (!Integer(offsetText, out var offset) || !Integer(limitText, out var limit) || limit < 1 || limit > 100)
            return Error(400, "invalid_request");
        long total = await users.CountAsync();
        var items = offset >= total ? [] : await users.ListAsync((long)offset, (int)limit);
        return Json(200, new UserPage(items.Select(User.From).ToArray(), offset, (int)limit, total, [new("id", "asc")]));
    }

    private static bool Integer(string text, out BigInteger value)
    {
        value = BigInteger.Zero;
        return text.Length > 0 && text.All(character => character is >= '0' and <= '9')
            && BigInteger.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
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
    int Limit, long Total, UserOrder[] Order);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UserOrder(string Field, string Direction);

public sealed class OffsetJsonConverter : JsonConverter<BigInteger>
{
    public override BigInteger Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("Offset is response metadata only.");
    public override void Write(Utf8JsonWriter writer, BigInteger value, JsonSerializerOptions options) =>
        writer.WriteRawValue(value.ToString(CultureInfo.InvariantCulture));
}
