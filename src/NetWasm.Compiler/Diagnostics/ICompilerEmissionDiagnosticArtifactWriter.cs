using System;

namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerEmissionDiagnosticArtifactWriter
{
    void WriteEmission(CompilerOptions options, ReadOnlySpan<byte> bytes);
}
