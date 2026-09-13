using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal interface IObjectLayoutResolver
{
    ObjectLayout Resolve(EntityKey type);

    ObjectLayout Resolve(CliTypeIdentity type);
}
