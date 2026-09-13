using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public interface ITypeIdentityResolver
{
    CliTypeIdentity GetTypeIdentity(EntityKey type);
}
