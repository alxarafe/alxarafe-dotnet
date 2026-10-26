using Alxarafe.Navigation;

namespace Alxarafe.Modules.Assets;

public sealed class AssetsNavigation : INavigationDefinitionProvider
{
    public const string Root = AssetsModule.Id + ".navigation";
    public const string Register = AssetsModule.Id + ".register";
    public const string Types = AssetsModule.Id + ".types";
    public const string Locations = AssetsModule.Id + ".locations";
    public IReadOnlyCollection<NavigationDefinition> GetNavigationDefinitions() =>
    [new(Root, AssetsModule.Id, AssetsNavigationMessages.Group, 100, labelResource: typeof(AssetsNavigationMessages)),
        new(Register, AssetsModule.Id, AssetsNavigationMessages.Register, 10, Root, new ResourceNavigationTarget(AssetResources.Asset), labelResource: typeof(AssetsNavigationMessages)),
        new(Types, AssetsModule.Id, AssetsNavigationMessages.Types, 20, Root, new ResourceNavigationTarget(AssetResources.Type), permissions: [AssetsPermissions.SchemaManage], labelResource: typeof(AssetsNavigationMessages)),
        new(Locations, AssetsModule.Id, AssetsNavigationMessages.Locations, 30, Root, new ResourceNavigationTarget(AssetResources.Location), labelResource: typeof(AssetsNavigationMessages))];
}
