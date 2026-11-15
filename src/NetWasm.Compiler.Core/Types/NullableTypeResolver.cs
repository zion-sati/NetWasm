// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

namespace NetWasm.Compiler.Core.Types;

public sealed class NullableTypeResolver : INullableTypeResolver
{
    public CliTypeIdentity? Resolve(CliTypeIdentity type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.Shape == CliTypeShape.GenericInstantiation &&
            type.ElementType!.FullName == "System.Nullable`1" &&
            type.TypeArguments.Length == 1
                ? type.TypeArguments[0]
                : null;
    }
}
