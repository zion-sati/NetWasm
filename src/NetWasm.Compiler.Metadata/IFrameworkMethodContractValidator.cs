using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed record FrameworkMethodBinding(
    MethodDefinitionModel Original,
    MethodDefinitionModel Target,
    string Caller,
    int IlOffset)
{
    public MetadataAssemblySnapshot? OriginalSource { get; init; }
}

internal interface IFrameworkMethodContractValidator
{
    void Validate(FrameworkMethodBinding binding);
}
