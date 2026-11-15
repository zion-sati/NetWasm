// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using Microsoft.Build.Framework;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tasks.MsBuild;

internal sealed class CompilerReferenceOriginReader : ICompilerReferenceOriginReader
{
    public ImmutableArray<CompilationReferenceOrigin> Read(
        ITaskItem[] references, CompilationReferenceRole role)
    {
        ArgumentNullException.ThrowIfNull(references);
        return references.Select(item => new CompilationReferenceOrigin(
            item.ItemSpec,
            role,
            ReadMetadata(item, "NuGetPackageId"),
            ReadMetadata(item, "NuGetPackageVersion"),
            ReadMetadata(item, "PathInPackage"),
            ReadMetadata(item, "NetWasmOriginalReference"))).ToImmutableArray();
    }

    private static string? ReadMetadata(ITaskItem item, string name)
    {
        var value = item.GetMetadata(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
