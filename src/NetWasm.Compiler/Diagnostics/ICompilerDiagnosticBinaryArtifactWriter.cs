using System;

namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerDiagnosticBinaryArtifactWriter
{
    string WriteBinary(string path, ReadOnlySpan<byte> value);
}
