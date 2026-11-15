// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerSourcePathProvider(
    ICompilerDiagnosticFileExistenceReader files) : ICompilerSourcePathProvider
{
    private readonly ICompilerDiagnosticFileExistenceReader _files =
        files ?? throw new ArgumentNullException(nameof(files));

    public ImmutableArray<string> Provide(CompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.SourcePaths.IsDefaultOrEmpty)
        {
            return
            [
                .. options.SourcePaths.Select(Path.GetFullPath)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal),
            ];
        }

        var adjacent = Path.ChangeExtension(options.EntryAssemblyPath, ".cs");
        return _files.Exists(adjacent) ? [Path.GetFullPath(adjacent)] : [];
    }
}
