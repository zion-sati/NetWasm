using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal interface IMetadataCallSiteSignatureResolver
{
    // The calli at offset in method (for diagnostics) reads the call site metadataToken names.
    CilOperand.CallSite Resolve(
        MetadataAssemblySnapshot source,
        int metadataToken,
        CliGenericContext genericContext,
        string method,
        int offset);
}
