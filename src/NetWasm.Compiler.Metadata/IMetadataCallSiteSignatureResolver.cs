using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal interface IMetadataCallSiteSignatureResolver
{
    MethodSignatureModel Resolve(
        MetadataAssemblySnapshot source,
        int metadataToken,
        CliGenericContext genericContext);
}
