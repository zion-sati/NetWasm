using System.Reflection.Metadata;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal interface IMetadataSignatureTypeResolver
{
    CliTypeIdentity Resolve(
        MetadataAssemblySnapshot source,
        EntityHandle handle,
        CliGenericContext? genericContext = null);
}
