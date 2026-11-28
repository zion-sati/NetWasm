// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.StructResults;

// The locals a method moves returned fields through: per value kind, as many as the most any one shape it returns or
// takes from a call holds.
internal sealed record StructResultLocals(ImmutableDictionary<CliValueKind, ImmutableArray<int>> ByKind)
{
    public static StructResultLocals Empty { get; } = new(ImmutableDictionary<CliValueKind, ImmutableArray<int>>.Empty);

    // Locals of the kinds given, numbered from first in that order, as a generated function declares them for one
    // shape: For of that shape then names them in order.
    public static StructResultLocals Sequential(int first, ImmutableArray<CliValueKind> kinds)
    {
        var byKind = new Dictionary<CliValueKind, ImmutableArray<int>.Builder>();
        for (var index = 0; index < kinds.Length; index++)
        {
            if (!byKind.TryGetValue(kinds[index], out var locals)) byKind.Add(kinds[index], locals = ImmutableArray.CreateBuilder<int>());
            locals.Add(first + index);
        }
        return new(byKind.ToImmutableDictionary(static pair => pair.Key, static pair => pair.Value.ToImmutable()));
    }

    // A local for each of the shape's fields, in field order.
    public ImmutableArray<int> For(StructResultShape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        var used = new Dictionary<CliValueKind, int>();
        var result = ImmutableArray.CreateBuilder<int>(shape.Fields.Length);
        foreach (var field in shape.Fields)
        {
            var index = used.GetValueOrDefault(field.Kind);
            used[field.Kind] = index + 1;
            if (!ByKind.TryGetValue(field.Kind, out var locals) || index >= locals.Length)
            {
                throw new InvalidOperationException(
                    $"No struct result local of kind {field.Kind} for {shape.Type.CanonicalName}.");
            }
            result.Add(locals[index]);
        }
        return result.MoveToImmutable();
    }
}
