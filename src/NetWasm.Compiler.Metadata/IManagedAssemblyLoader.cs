// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;

namespace NetWasm.Compiler.Metadata;

public interface IManagedAssemblyLoader
{
    ManagedAssembly Load(
        string path,
        ImmutableDictionary<string, string> referenceAssemblyAliases,
        ImmutableDictionary<(AssemblyIdentity Assembly, string FullName), AssemblyIdentity>? typeForwarders = null,
        INativeImportBinder? nativeBinding = null);
}
