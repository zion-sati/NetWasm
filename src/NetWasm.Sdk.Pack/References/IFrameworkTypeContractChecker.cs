using Mono.Cecil;

namespace NetWasm.Sdk.Pack.References;

public enum FrameworkTypeMismatch
{
    None,
    Declaration,
    RefLike,
    GenericConstraints,
    EnumStorage,
    EnumValues,
    Inheritance
}

public sealed record FrameworkTypeContract(
    TypeDefinition Source,
    TypeDefinition Target,
    IReadOnlyDictionary<string, TypeDefinition> PlatformTypes,
    FrameworkContractScope Scope);

public interface IFrameworkTypeContractChecker
{
    FrameworkTypeMismatch Check(FrameworkTypeContract request);
}
