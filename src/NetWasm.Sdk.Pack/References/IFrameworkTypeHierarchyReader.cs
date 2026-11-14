using Mono.Cecil;

namespace NetWasm.Sdk.Pack.References;

public sealed record FrameworkTypeHierarchyRequest(
    TypeDefinition Root,
    IReadOnlyDictionary<string, TypeDefinition> PlatformTypes,
    FrameworkContractScope Scope);

public sealed record FrameworkTypeHierarchy(
    IReadOnlyList<TypeReference> BaseTypes,
    IReadOnlyList<TypeReference> Interfaces);

/// <summary>Reads admitted inheritance edges with their generic substitutions.</summary>
public interface IFrameworkTypeHierarchyReader
{
    FrameworkTypeHierarchy Read(FrameworkTypeHierarchyRequest request);
}
