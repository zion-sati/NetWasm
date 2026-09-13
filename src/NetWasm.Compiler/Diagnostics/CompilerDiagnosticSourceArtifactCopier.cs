using System;
using System.IO;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerDiagnosticSourceArtifactCopier :
    ICompilerDiagnosticSourceArtifactCopier
{
    public void CopySource(FileInfo source, string destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        source.CopyTo(destination, overwrite: true);
    }
}
