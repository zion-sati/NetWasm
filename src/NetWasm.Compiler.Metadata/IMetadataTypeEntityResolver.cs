using System.Reflection.Metadata;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal interface IMetadataTypeEntityResolver
{
    EntityKey Resolve(MetadataAssemblySnapshot source, EntityHandle handle);
}
