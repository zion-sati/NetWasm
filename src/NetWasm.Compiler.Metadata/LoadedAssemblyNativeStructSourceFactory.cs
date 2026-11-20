// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;

namespace NetWasm.Compiler.Metadata;

// Each compilation's source over the assemblies it has read (LoadedAssemblyNativeStructSource).
public sealed class LoadedAssemblyNativeStructSourceFactory : ILoadedAssemblyNativeStructSourceFactory
{
    public INativeStructSource Create(ImmutableDictionary<string, ManagedAssembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        return new LoadedAssemblyNativeStructSource(assemblies);
    }
}
