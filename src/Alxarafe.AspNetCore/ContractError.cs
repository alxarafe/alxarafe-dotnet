using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Alxarafe.AspNetCore;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record ContractError([property: Required] string Code)
{
    [Required, MinLength(1)]
    public IReadOnlyList<ContractErrorDetail> Errors { get; init; } = [new(Code)];
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ContractErrorDetail([property: Required] string Code,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Resource = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ResourceId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Path = null);
