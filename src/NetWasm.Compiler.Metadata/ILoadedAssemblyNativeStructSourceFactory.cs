// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Metadata;

// Makes the struct source over every assembly a compilation has read, from which the P/Invokes and unmanaged calli
// signatures whose structs reach across assemblies lay out (ManagedAssembly.LayCrossAssemblyStructStubs).
public interface ILoadedAssemblyNativeStructSourceFactory
{
    INativeStructSource Create(ImmutableDictionary<string, ManagedAssembly> assemblies);
}
