// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Testing.CompilerHost;

internal interface ICompilerHostArtifactWriter
{
    void Write(ImmutableArray<CompilerHostArtifact> artifacts);
}

internal sealed class CompilerHostArtifactWriter : ICompilerHostArtifactWriter
{
    public void Write(ImmutableArray<CompilerHostArtifact> artifacts)
    {
        if (artifacts.IsDefault)
            throw new ArgumentException("compiler artifacts must be initialized", nameof(artifacts));
        foreach (var artifact in artifacts)
            File.WriteAllBytes(artifact.Path, artifact.Bytes.AsSpan());
    }
}
