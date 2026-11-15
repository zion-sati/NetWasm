// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Text.Json;

namespace NetWasm.Compiler.Tasks.Artifacts;

internal sealed class CompilerArtifactManifestWriter : ICompilerArtifactManifestWriter
{
    public void Write(CompilerArtifactManifestWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(request.Path))!);
        File.WriteAllText(request.Path, JsonSerializer.Serialize(request.Manifest, CompilerArtifactManifestJsonContext.Default.CompilerArtifactManifest));
    }
}
