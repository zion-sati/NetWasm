namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerRootMapSnapshotBuilder
{
    CompilerRootMapsArtifact BuildRootMaps(NetWasm.Compiler.ReachableProgram program);
}
