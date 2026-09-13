using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal interface IManagedObjectLayoutBuilder
{
    ObjectLayout Build(EntityKey type);
    ObjectLayout Build(CliTypeIdentity type);
}
