// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Sdk.Pack.Archives;

public sealed record ArchiveEntry(string Path, ImmutableArray<byte> Content, string? SourcePath = null)
{
    public static ArchiveEntry FromBytes(string path, ReadOnlySpan<byte> content, string? sourcePath = null) =>
        new(path, content.ToArray().ToImmutableArray(), sourcePath);
}
