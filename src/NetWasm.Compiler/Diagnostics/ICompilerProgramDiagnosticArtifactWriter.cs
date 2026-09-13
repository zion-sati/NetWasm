using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerProgramDiagnosticArtifactWriter
{
    void WriteProgram(CompilerOptions options, ISymbolFormatter symbols, ReachableProgram program);
}
