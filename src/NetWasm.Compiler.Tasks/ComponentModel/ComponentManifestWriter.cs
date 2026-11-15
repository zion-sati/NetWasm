// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Text.Json;

namespace NetWasm.Compiler.Tasks.ComponentModel;

internal sealed class ComponentManifestWriter : IComponentManifestWriter
{
    public void Write(ComponentManifestWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(request.Path))!);
        File.WriteAllText(
            request.Path,
            JsonSerializer.Serialize(request.Manifest, ComponentManifestJsonContext.Default.ComponentManifest));
    }
}
