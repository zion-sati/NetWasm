// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

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
