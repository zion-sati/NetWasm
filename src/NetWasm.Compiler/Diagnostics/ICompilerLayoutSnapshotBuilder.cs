using NetWasm.Compiler.Layout;

namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerLayoutSnapshotBuilder
{
    object BuildLayouts(ManagedLayoutSnapshot layouts);
}
