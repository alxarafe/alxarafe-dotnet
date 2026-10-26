namespace Alxarafe.Modules.Assets;

public sealed class AssetAssignmentRow
{
    public Guid Id { get; set; }
    public Guid AssetId { get; set; }
    public string TargetType { get; set; } = AssetAssignmentTargets.Location;
    public Guid TargetId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public string? StartReason { get; set; }
    public string? EndReason { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
