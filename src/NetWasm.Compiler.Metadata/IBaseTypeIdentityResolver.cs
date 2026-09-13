using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public interface IBaseTypeIdentityResolver
{
    CliTypeIdentity? GetBaseTypeIdentity(CliTypeIdentity type);
}
