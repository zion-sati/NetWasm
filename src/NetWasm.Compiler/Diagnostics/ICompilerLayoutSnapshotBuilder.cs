using NetWasm.Compiler.Layout;

namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerLayoutSnapshotBuilder
{
    CompilerLayoutsArtifact BuildLayouts(ManagedLayoutSnapshot layouts);
}
