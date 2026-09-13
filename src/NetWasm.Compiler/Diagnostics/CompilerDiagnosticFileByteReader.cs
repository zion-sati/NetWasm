using System;
using System.IO;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerDiagnosticFileByteReader : ICompilerDiagnosticFileByteReader
{
    public byte[] ReadBytes(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return File.ReadAllBytes(path);
    }
}
