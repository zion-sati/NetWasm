using System.Reflection.Metadata;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal interface IMetadataTypeResolver
{
    TypeDefinitionModel Resolve(MetadataAssemblySnapshot source, EntityHandle handle);
}
