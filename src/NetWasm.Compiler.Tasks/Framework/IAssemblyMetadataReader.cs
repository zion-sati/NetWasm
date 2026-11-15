// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tasks.Framework;

// An assembly's version and the simple names of the assemblies it references; null for a file without metadata.
internal interface IAssemblyMetadataReader
{
    AssemblyMetadataSummary? Read(string assemblyPath);
}

internal sealed record AssemblyMetadataSummary(Version Version, IReadOnlyList<string> References);
