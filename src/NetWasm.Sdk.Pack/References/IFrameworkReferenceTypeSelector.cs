using Mono.Cecil;

namespace NetWasm.Sdk.Pack.References;

public sealed record FrameworkReferenceSelection(
    string PackageId,
    AssemblyDefinition Implementation,
    AssemblyDefinition Platform);

/// <summary>Selects framework-owned overlaps; does not certify member compatibility.</summary>
public interface IFrameworkReferenceTypeSelector
{
    IReadOnlyList<string> SelectTypes(FrameworkReferenceSelection request);
}
