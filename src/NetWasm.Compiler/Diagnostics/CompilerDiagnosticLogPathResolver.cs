using System;
using System.IO;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerDiagnosticLogPathResolver : ICompilerDiagnosticLogPathResolver
{
    public string? Resolve(CompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!string.IsNullOrWhiteSpace(options.DiagnosticLogPath))
        {
            return options.DiagnosticLogPath;
        }

        return string.IsNullOrWhiteSpace(options.DiagnosticTracePath)
            ? null
            : Path.Combine(
                options.DiagnosticTracePath + ".passes",
                "compiler.log.jsonl");
    }
}
