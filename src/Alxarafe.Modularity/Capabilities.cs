using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Alxarafe.Modularity;

public sealed record CapabilitySchema([property: JsonPropertyName("$ref")] string Ref);
public sealed record CapabilityDescriptor(string Id, string Module, string Version, string Name, string Description,
    string Effect, CapabilitySchema InputSchema, CapabilitySchema OutputSchema, string[] RequiredPermissions);

public interface ICapabilityProvider
{
    IEnumerable<CapabilityDescriptor> GetCapabilities();
    IEnumerable<string> GetSchemaReferences();
}

public sealed class CapabilityRegistry
{
    private const string IdPattern = @"\A[a-z][a-z0-9]*(?:-[a-z0-9]+)*(?:\.[a-z][a-z0-9]*(?:-[a-z0-9]+)*){2}\z";
    private const string VersionPattern = @"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z";
    private const string SchemaPattern = @"\A#/components/schemas/[A-Za-z_][A-Za-z0-9_]*\z";
    public static bool IsValidId(string id) => Regex.IsMatch(id, IdPattern);
    public IReadOnlyList<CapabilityDescriptor> Capabilities { get; }

    public CapabilityRegistry(ModuleRegistry modules)
    {
        var descriptors = new SortedDictionary<string, CapabilityDescriptor>(StringComparer.Ordinal);
        var schemaReferences = modules.Modules.OfType<ICapabilityProvider>()
            .SelectMany(provider => provider.GetSchemaReferences()).ToHashSet(StringComparer.Ordinal);
        if (schemaReferences.Any(reference => reference is null || !Regex.IsMatch(reference, SchemaPattern)))
            throw new InvalidOperationException("Invalid capability schema reference registration.");
        foreach (var module in modules.Modules)
        {
            if (module is not ICapabilityProvider provider) continue;
            foreach (var capability in provider.GetCapabilities())
            {
                if (capability is null || capability.Id is null || !IsValidId(capability.Id)
                    || capability.Module != module.Descriptor.Id || !Regex.IsMatch(capability.Version ?? "", VersionPattern)
                    || string.IsNullOrWhiteSpace(capability.Name) || string.IsNullOrWhiteSpace(capability.Description)
                    || capability.Effect is not ("read" or "write")
                    || capability.InputSchema is null || !schemaReferences.Contains(capability.InputSchema.Ref)
                    || capability.OutputSchema is null || !schemaReferences.Contains(capability.OutputSchema.Ref)
                    || capability.RequiredPermissions is null || capability.RequiredPermissions.Any(string.IsNullOrWhiteSpace)
                    || !capability.RequiredPermissions.SequenceEqual(capability.RequiredPermissions.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
                    || descriptors.ContainsKey(capability.Id))
                    throw new InvalidOperationException("Invalid or duplicate capability registration.");
                descriptors.Add(capability.Id, capability);
            }
        }
        Capabilities = Array.AsReadOnly(descriptors.Values.ToArray());
    }
}
