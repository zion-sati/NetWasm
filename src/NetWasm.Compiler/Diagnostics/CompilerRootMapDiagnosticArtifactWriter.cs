// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerRootMapDiagnosticArtifactWriter(
    ICompilerDiagnosticArtifactPathResolver paths,
    ICompilerDiagnosticJsonArtifactWriter json,
    ICompilerRootMapSnapshotBuilder snapshot) : ICompilerRootMapDiagnosticArtifactWriter
{
    private readonly ICompilerDiagnosticArtifactPathResolver _paths =
        paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly ICompilerDiagnosticJsonArtifactWriter _json =
        json ?? throw new ArgumentNullException(nameof(json));
    private readonly ICompilerRootMapSnapshotBuilder _snapshot =
        snapshot ?? throw new ArgumentNullException(nameof(snapshot));

    public void WriteRootMaps(CompilerOptions options, ReachableProgram program)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(program);
        var path = _paths.ResolvePath(options, "05-root-maps.json");
        if (path is not null)
        {
            _json.WriteJson(path, _snapshot.BuildRootMaps(program), CompilerDiagnosticArtifactJsonContext.Default.CompilerRootMapsArtifact);
        }
    }
}
