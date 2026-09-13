using NetWasm.Compiler.Core;
using NetWasm.Compiler.EntryPoints;

namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerEntryDiagnosticArtifactWriter
{
    void WriteEntry(CompilerOptions options, ISymbolFormatter symbols, CompilationEntry entry);
}
