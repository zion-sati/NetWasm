using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission;

internal interface ICilTypeIdentityResolver
{
    CliTypeIdentity Resolve(EntityKey key);
}
