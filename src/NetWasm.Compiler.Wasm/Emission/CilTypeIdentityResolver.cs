using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.Wasm.Emission;

internal sealed class CilTypeIdentityResolver(
    ITypeRepository types) :
    ICilTypeIdentityResolver
{
    private readonly ITypeRepository _types = types ??
        throw new ArgumentNullException(nameof(types));
    public CliTypeIdentity Resolve(EntityKey key)
    {
        var type = _types.GetTypeDefinition(key);
        return CliTypeIdentity.Named(
            key.Assembly,
            type.Namespace,
            type.Name,
            type.IsValueType);
    }
}
