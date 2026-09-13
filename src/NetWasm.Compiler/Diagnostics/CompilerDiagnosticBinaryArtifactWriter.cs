using System;
using System.IO;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerDiagnosticBinaryArtifactWriter :
    ICompilerDiagnosticBinaryArtifactWriter
{
    public string WriteBinary(string path, ReadOnlySpan<byte> value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllBytes(path, value.ToArray());
        return path;
    }
}
