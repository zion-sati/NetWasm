// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace NetWasm.Compiler.Tasks.Framework;

internal sealed class AssemblyMetadataReader : IAssemblyMetadataReader
{
    public AssemblyMetadataSummary? Read(string assemblyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        using var stream = File.OpenRead(assemblyPath);
        using var image = new PEReader(stream);
        if (!image.HasMetadata)
        {
            return null;
        }
        var metadata = image.GetMetadataReader();
        return new(
            metadata.GetAssemblyDefinition().Version,
            [.. metadata.AssemblyReferences
                .Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name))]);
    }
}
