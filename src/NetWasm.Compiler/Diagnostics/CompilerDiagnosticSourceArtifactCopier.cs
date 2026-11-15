// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

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
