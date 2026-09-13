using System;
using System.IO;
using System.Text;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerDiagnosticTextWriterFactory : ICompilerDiagnosticTextWriterFactory
{
    public TextWriter Create(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var writer = new StreamWriter(
            new FileStream(fullPath, FileMode.Append, FileAccess.Write, FileShare.Read),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.NewLine = "\n";
        return writer;
    }
}
