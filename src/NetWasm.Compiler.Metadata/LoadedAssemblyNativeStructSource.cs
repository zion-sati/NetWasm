// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

// A value type of any assembly the compilation has read, by the assembly its identity names, for laying out the
// P/Invokes and unmanaged calli signatures whose structs reach across assemblies before the compilation's snapshot.
internal sealed class LoadedAssemblyNativeStructSource(ImmutableDictionary<string, ManagedAssembly> assemblies)
    : INativeStructSource
{
    public (TypeDefinitionModel Definition, ImmutableArray<FieldDefinitionModel> Fields)? Describe(CliTypeIdentity type) =>
        type.Assembly is { } assembly && assemblies.TryGetValue(assembly.Name, out var declaring)
            ? declaring.DescribeValueType(type)
            : null;
}
