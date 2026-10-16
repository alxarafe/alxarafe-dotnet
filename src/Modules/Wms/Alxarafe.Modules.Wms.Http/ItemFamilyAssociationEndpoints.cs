using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Text.Json;
using System.Text.Json.Serialization;
using Alxarafe.AspNetCore;
using Alxarafe.Modules.Wms.Application;
using Alxarafe.Security.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Alxarafe.Modules.Wms.Http;

public static class ItemFamilyAssociationEndpoints
{
    public const string AttributePathSuffix = "/storage-attributes";
    public const string Path = ItemFamilyEndpoints.Path + "/{familyId}" + AttributePathSuffix;
    public const string AttributeIdField = "storageAttributeId";
    public const string AttributeIdsField = "storageAttributeIds";
    public const string AddField = "add";
    public const string RemoveField = "remove";
    public const string FamilyResource = "wms_item_family";
    public const string AttributeResource = "wms_storage_attribute";
    public const string AssociationResource = "wms_item_family_storage_attribute";
    public const string FamilyMissing = "wms_item_family_not_found";
    public const string AttributeMissing = "wms_storage_attribute_not_found";
    public const string AssociationMissing = "wms_item_family_storage_attribute_not_found";
    public const string MultipleMissing = "wms_item_family_storage_attributes_not_found";
    public const string AlreadyAssociated = "wms_item_family_storage_attribute_already_exists";
    public const string NotAssociated = "wms_item_family_storage_attribute_not_associated";
    public const string CreateConflict = "wms_item_family_storage_attribute_conflict";
    public const string ChangeConflict = "wms_item_family_storage_attributes_conflict";
    private static readonly IReadOnlyList<CollectionOrder> CodeAscending = Array.AsReadOnly(new CollectionOrder[] { new(StorageAttributeEndpoints.CodeField, "asc") });

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(Path).RequireAuthorization(CoreAdministration.Policy).WithSharedContractErrors().WithTags("WMS");
        foreach (int status in new[] { StatusCodes.Status400BadRequest, StatusCodes.Status401Unauthorized, StatusCodes.Status403Forbidden, StatusCodes.Status404NotFound })
            group.WithMetadata(new ProducesResponseTypeMetadata(status, typeof(ContractError), [MediaTypeNames.Application.Json]));
        group.MapGet("", (HttpContext context, ItemFamilyAssociations associations, string familyId) =>
            ExecuteAsync(context, associations, familyId, AssociationOperation.List)).Produces<StorageAttributePage>();
        group.MapPost("", (HttpContext context, ItemFamilyAssociations associations, string familyId) =>
            ExecuteAsync(context, associations, familyId, AssociationOperation.Create))
            .Accepts<CreateItemFamilyAssociationRequest>(MediaTypeNames.Application.Json).Produces<ItemFamilyAssociationResponse>(StatusCodes.Status201Created).Produces<ContractError>(StatusCodes.Status409Conflict);
        group.MapPut("", (HttpContext context, ItemFamilyAssociations associations, string familyId) =>
            ExecuteAsync(context, associations, familyId, AssociationOperation.Replace))
            .Accepts<ReplaceItemFamilyAssociationsRequest>(MediaTypeNames.Application.Json).Produces(StatusCodes.Status204NoContent);
        group.MapPatch("", (HttpContext context, ItemFamilyAssociations associations, string familyId) =>
            ExecuteAsync(context, associations, familyId, AssociationOperation.Change))
            .Accepts<ChangeItemFamilyAssociationsRequest>(MediaTypeNames.Application.Json).Produces(StatusCodes.Status204NoContent).Produces<ContractError>(StatusCodes.Status409Conflict);
        group.MapDelete("/{storageAttributeId}", (HttpContext context, ItemFamilyAssociations associations, string familyId, string storageAttributeId) =>
            ExecuteAsync(context, associations, familyId, AssociationOperation.Delete, storageAttributeId)).Produces(StatusCodes.Status204NoContent);
    }

    private static async Task<IResult> ExecuteAsync(HttpContext context, ItemFamilyAssociations associations,
        string familyId, AssociationOperation operation, string? storageAttributeId = null)
    {
        if (!CanonicalId(familyId, out var family)) return Invalid();
        AssociationCommand? command;
        if (operation == AssociationOperation.List)
        {
            if (context.Request.Query.Any(entry => entry.Value.Count != 1
                || entry.Key is not (CollectionPagination.OffsetParameter or CollectionPagination.LimitParameter))) return Invalid();
            var page = CollectionPagination.Read(context.Request.Query);
            if (page is null) return Invalid();
            command = new(operation, [], [], page.Offset, page.Limit);
        }
        else
        {
            if (context.Request.Query.Count != 0) return Invalid();
            if (operation == AssociationOperation.Delete)
            {
                if (!CanonicalId(storageAttributeId, out var attribute)) return Invalid();
                command = new(operation, [], [new(attribute)]);
            }
            else
            {
                try
                {
                    using var document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
                    command = Read(document.RootElement, operation);
                }
                catch (JsonException) { return Invalid(); }
                catch (InvalidOperationException) { return Invalid(); }
                catch (BadHttpRequestException) { return Invalid(); }
                if (command is null) return Invalid();
            }
        }
        var result = await associations.ExecuteAsync(family, command, context.RequestAborted);
        if (result.Failures.Count != 0) return Failure(result.Failures, operation);
        if (operation == AssociationOperation.List)
            return Results.Json(new StorageAttributePage(result.Items.Select(StorageAttributeResponse.From).ToArray(), command.Offset, command.Limit, result.Total, CodeAscending));
        if (operation == AssociationOperation.Create)
        {
            var attribute = result.Items.Single();
            context.Response.Headers.Location = ItemFamilyEndpoints.Path + "/" + family + AttributePathSuffix + "/" + attribute.Id;
            return Results.Json(new ItemFamilyAssociationResponse(family, StorageAttributeResponse.From(attribute)), statusCode: StatusCodes.Status201Created);
        }
        return Results.NoContent();
    }

    private static AssociationCommand? Read(JsonElement root, AssociationOperation operation)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        var properties = root.EnumerateObject().ToArray();
        if (properties.Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length) return null;
        if (operation == AssociationOperation.Create)
        {
            if (properties.Length != 1 || properties[0].Name != AttributeIdField || properties[0].Value.ValueKind != JsonValueKind.String
                || !CanonicalId(properties[0].Value.GetString(), out var id)) return null;
            return new(operation, [new(id, "/" + AttributeIdField)], []);
        }
        if (operation == AssociationOperation.Replace)
        {
            if (properties.Length != 1 || properties[0].Name != AttributeIdsField) return null;
            var ids = References(properties[0].Value, AttributeIdsField);
            return ids is null ? null : new(operation, ids, []);
        }
        if (properties.Any(property => property.Name is not (AddField or RemoveField))) return null;
        var add = root.TryGetProperty(AddField, out var addValue) ? References(addValue, AddField) : [];
        var remove = root.TryGetProperty(RemoveField, out var removeValue) ? References(removeValue, RemoveField) : [];
        return add is null || remove is null || add.Count + remove.Count == 0 ? null : new(operation, add, remove);
    }

    private static List<AttributeReference>? References(JsonElement value, string field)
    {
        if (value.ValueKind != JsonValueKind.Array) return null;
        var references = new List<AttributeReference>();
        var unique = new HashSet<Guid>();
        foreach (var element in value.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String || !CanonicalId(element.GetString(), out var id) || !unique.Add(id)) return null;
            references.Add(new(id, "/" + field + "/" + references.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        return references;
    }

    private static bool CanonicalId(string? text, out Guid id) => Guid.TryParseExact(text, "D", out id)
        && id != Guid.Empty && string.Equals(text, id.ToString("D"), StringComparison.Ordinal);

    private static IResult Invalid() => Results.Json(new ContractError(ItemFamilyEndpoints.InvalidRequest), statusCode: StatusCodes.Status400BadRequest);

    private static IResult Failure(IReadOnlyList<AssociationFailure> failures, AssociationOperation operation)
    {
        var details = failures.Select(failure => new ContractErrorDetail(failure.Issue switch
        {
            AssociationIssue.FamilyMissing => FamilyMissing,
            AssociationIssue.AttributeMissing => AttributeMissing,
            AssociationIssue.AssociationMissing => AssociationMissing,
            AssociationIssue.AlreadyAssociated => AlreadyAssociated,
            AssociationIssue.NotAssociated => NotAssociated,
            _ => throw new InvalidOperationException("Unknown association failure.")
        }, failure.Issue switch
        {
            AssociationIssue.FamilyMissing => FamilyResource,
            AssociationIssue.AssociationMissing => AssociationResource,
            _ => AttributeResource
        }, failure.Id.ToString("D"), failure.Path)).ToArray();
        bool missing = failures[0].Issue is AssociationIssue.FamilyMissing or AssociationIssue.AttributeMissing or AssociationIssue.AssociationMissing;
        string code = missing ? details.Length == 1 ? details[0].Code : MultipleMissing
            : operation == AssociationOperation.Create ? CreateConflict : ChangeConflict;
        return Results.Json(new ContractError(code) { Errors = details },
            statusCode: missing ? StatusCodes.Status404NotFound : StatusCodes.Status409Conflict);
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateItemFamilyAssociationRequest([property: Required] Guid StorageAttributeId);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReplaceItemFamilyAssociationsRequest([property: Required] Guid[] StorageAttributeIds);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChangeItemFamilyAssociationsRequest(Guid[]? Add, Guid[]? Remove);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ItemFamilyAssociationResponse([property: Required] Guid ItemFamilyId, [property: Required] StorageAttributeResponse StorageAttribute);
