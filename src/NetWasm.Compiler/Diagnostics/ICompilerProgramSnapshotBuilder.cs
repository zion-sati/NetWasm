using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerProgramSnapshotBuilder
{
    object BuildProgram(ISymbolFormatter symbols, ReachableProgram program);
}
