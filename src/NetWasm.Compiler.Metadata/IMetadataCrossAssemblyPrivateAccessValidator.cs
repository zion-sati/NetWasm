using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed record MetadataMemberAccessRequest(
    AssemblyIdentity CallerAssembly,
    EntityKey Member,
    string Caller,
    int IlOffset);

internal interface IMetadataCrossAssemblyPrivateAccessValidator
{
    void Validate(MetadataMemberAccessRequest request);
}
