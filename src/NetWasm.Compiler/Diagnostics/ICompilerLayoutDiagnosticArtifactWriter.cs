using NetWasm.Compiler.Layout;

namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerLayoutDiagnosticArtifactWriter
{
    void WriteLayouts(CompilerOptions options, ManagedLayoutSnapshot layouts);
}
