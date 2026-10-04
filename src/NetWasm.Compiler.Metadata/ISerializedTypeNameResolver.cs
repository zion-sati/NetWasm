using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal interface ISerializedTypeNameResolver
{
    CliTypeIdentity? Resolve(string? name, MetadataAssemblySnapshot source);
}
