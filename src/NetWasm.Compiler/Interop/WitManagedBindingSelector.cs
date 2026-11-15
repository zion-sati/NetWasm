// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Interop;

internal interface IWitManagedBindingSelector
{
    ImmutableArray<MethodDefinitionModel> Select(
        MetadataCompilationSnapshot metadata,
        IEnumerable<MethodDefinitionModel> candidates);
}

internal sealed class WitManagedBindingSelector : IWitManagedBindingSelector
{
    public ImmutableArray<MethodDefinitionModel> Select(
        MetadataCompilationSnapshot metadata,
        IEnumerable<MethodDefinitionModel> candidates)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(candidates);
        var all = candidates.ToImmutableArray();
        var owned = all
            .Where(method => method.Key.Assembly == metadata.EntryAssemblyIdentity)
            .ToImmutableArray();
        return [.. (owned.IsEmpty ? all : owned)
            .OrderBy(method => method.Key.Assembly.Name, StringComparer.Ordinal)
            .ThenBy(method => method.Key.MetadataToken)];
    }
}
