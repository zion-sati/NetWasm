using Mono.Cecil;

namespace NetWasm.Sdk.Pack.References;

/// <summary>
/// Framework ownership policy for restored standard backports. Public-key tokens
/// establish binding identity, not cryptographic publisher authentication. Versions
/// are preserved by the view writer; compatibility is checked on actual contracts.
/// </summary>
public sealed class FrameworkReferenceTypeSelector : IFrameworkReferenceTypeSelector
{
    public IReadOnlyList<string> SelectTypes(FrameworkReferenceSelection request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.PackageId);
        ArgumentNullException.ThrowIfNull(request.Implementation);
        ArgumentNullException.ThrowIfNull(request.Platform);
        var identity = request.Implementation.Name;
        if (!FrameworkAssemblyIdentities.ReferencePackages.TryGetValue(identity.Name, out var token) ||
            !string.Equals(request.PackageId, identity.Name, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(identity.Culture) ||
            !string.Equals(Convert.ToHexString(identity.PublicKeyToken), token, StringComparison.OrdinalIgnoreCase))
            return [];
        if (request.Platform.Name.Name != "NetWasm.CoreLib")
            throw new ArgumentException("Framework overlaps require the NetWasm.CoreLib owner.", nameof(request));

        var owners = request.Platform.MainModule.GetTypes().Where(IsVisible)
            .Select(type => type.FullName).ToHashSet(StringComparer.Ordinal);
        return request.Implementation.MainModule.GetTypes()
            .Where(type => IsVisible(type) && type.FullName.StartsWith("System.", StringComparison.Ordinal) &&
                owners.Contains(type.FullName))
            .Select(type => type.FullName).Order(StringComparer.Ordinal).ToArray();
    }

    private static bool IsVisible(TypeDefinition type) => type.IsPublic ||
        (type.IsNestedPublic || type.IsNestedFamily || type.IsNestedFamilyOrAssembly) &&
        type.DeclaringType is { } parent && IsVisible(parent);
}
