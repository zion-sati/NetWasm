using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerProgramSnapshotBuilder
{
    CompilerProgramArtifact BuildProgram(ISymbolFormatter symbols, ReachableProgram program);
}
