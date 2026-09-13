using System;
using System.IO;
using System.Text;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerDiagnosticTextArtifactWriter :
    ICompilerDiagnosticTextArtifactWriter
{
    public void WriteText(string path, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(value);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(path, value, new UTF8Encoding(false));
    }
}
