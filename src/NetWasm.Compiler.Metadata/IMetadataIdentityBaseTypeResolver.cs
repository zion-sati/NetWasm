using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public interface IMetadataIdentityBaseTypeResolver
{
    CliTypeIdentity? GetBaseType(CliTypeIdentity type);
}
