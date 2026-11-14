using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed record FrameworkFieldBinding(
    FieldDefinitionModel Original,
    FieldDefinitionModel Target,
    string Caller,
    int IlOffset)
{
    public MetadataAssemblySnapshot? OriginalSource { get; init; }
}

internal interface IFrameworkFieldContractValidator
{
    void Validate(FrameworkFieldBinding binding);
}
