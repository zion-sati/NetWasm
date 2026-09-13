using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal interface IMetadataAssemblyResolver
{
    MetadataAssemblySnapshot Resolve(AssemblyIdentity identity);
}
