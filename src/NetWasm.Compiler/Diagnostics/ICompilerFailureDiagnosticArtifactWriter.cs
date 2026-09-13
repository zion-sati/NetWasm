using System;

namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerFailureDiagnosticArtifactWriter
{
    void WriteFailure(CompilerOptions options, string stage, Exception exception);
}
