using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal interface ICilDecoderFactory
{
    ICilDecoder Create();
}

internal sealed class CilDecoderFactory(
    IMetadataMethodBodyBlockReader methodBodies,
    ISymbolFormatter symbols,
    IMetadataMethodReferenceResolver methodReferences,
    IMetadataTypeEntityResolver typeEntities,
    IMetadataFieldReferenceResolver fieldReferences,
    IMetadataTypeSignatureResolver typeSignatures,
    IMetadataCallSiteSignatureResolver callSiteSignatures,
    IMetadataStackTypeResolver stackTypes) : ICilDecoderFactory
{
    public ICilDecoder Create() =>
        new CilDecoder(
            methodBodies,
            symbols,
            methodReferences,
            typeEntities,
            fieldReferences,
            typeSignatures,
            callSiteSignatures,
            stackTypes,
            new CilSwitchLowerer());
}
