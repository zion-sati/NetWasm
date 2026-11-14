using System.Collections.Frozen;
using Mono.Cecil;

namespace NetWasm.Sdk.Pack.References;

public sealed record FrameworkContractScope(
    AssemblyNameReference Source,
    AssemblyNameReference Platform,
    FrozenSet<string> ForwardedNames,
    FrozenSet<string> PlatformTypeNames);

public interface IFrameworkContractTypeComparer
{
    bool Compare(TypeReference? source, TypeReference? target, FrameworkContractScope scope);
}
