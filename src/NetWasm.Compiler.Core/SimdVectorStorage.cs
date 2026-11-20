// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

namespace NetWasm.Compiler.Core;

// How many bytes of its v128 a vector type stores: Vector64<T> and Vector2 the low 8, Vector3 the low 12, and the
// others all 16. A narrower vector's lanes above its storage are undefined, as RyuJIT leaves a TYP_SIMD8's.
public static class SimdVectorStorage
{
    public static int Width(CliTypeIdentity type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return (type.Shape == CliTypeShape.GenericInstantiation ? type.ElementType!.FullName : type.FullName) switch
        {
            "System.Numerics.Vector2" or "System.Runtime.Intrinsics.Vector64`1" => 8,
            "System.Numerics.Vector3" => 12,
            _ => 16,
        };
    }
}
