// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.IO;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerDiagnosticArtifactPathResolver :
    ICompilerDiagnosticArtifactPathResolver
{
    public string? ResolvePath(CompilerOptions options, string artifactName)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactName);
        return options.DiagnosticTracePath is null
            ? null
            : Path.Combine(options.DiagnosticTracePath + ".passes", artifactName);
    }
}
