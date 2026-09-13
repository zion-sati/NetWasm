using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerDiagnosticArtifactEnumerator :
    ICompilerDiagnosticArtifactEnumerator
{
    public IReadOnlyList<string> Enumerate(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(directory, path))
                .Order(StringComparer.Ordinal)
                .ToArray()
            : [];
    }
}
