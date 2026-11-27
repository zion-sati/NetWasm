// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.StructResults;

// A value type's fields, flattened through the value types it nests, in offset order: each is one Wasm result, loaded
// from and stored to its offset as a field of its type and size is.
internal sealed record StructResultShape(CliTypeIdentity Type, ImmutableArray<StructResultField> Fields)
{
    public ImmutableArray<CliValueKind> Results { get; } = [.. Fields.Select(static field => field.Kind)];

    // The lane holding the field at the offset, when the field is that lane's own field (a struct field inside the
    // value spans several lanes, so a whole load of it has no single lane), or -1.
    public int LaneOf(int offset, CliTypeIdentity type)
    {
        for (var lane = 0; lane < Fields.Length; lane++)
        {
            if (Fields[lane].Offset == offset && Fields[lane].Type.CanonicalName == type.CanonicalName) return lane;
        }
        return -1;
    }
}

internal readonly record struct StructResultField(int Offset, int Size, CliTypeIdentity Type, CliValueKind Kind);
