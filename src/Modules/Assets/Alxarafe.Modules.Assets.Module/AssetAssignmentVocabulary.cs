namespace Alxarafe.Modules.Assets;

public static class AssetAssignmentTargets
{
    public const string Location = "LOCATION";
}

public static class AssetAssignmentLimits
{
    public const int ReasonLength = 255;
    public const int NotesLength = 4000;
    public const string ReasonPattern = @"\A[^\x00]*[^\s\x00][^\x00]*\z";
    public const string NotesPattern = @"\A[^\x00]*\z";
}
