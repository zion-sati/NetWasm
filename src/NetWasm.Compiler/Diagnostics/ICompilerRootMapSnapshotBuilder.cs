namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerRootMapSnapshotBuilder
{
    object BuildRootMaps(NetWasm.Compiler.ReachableProgram program);
}
