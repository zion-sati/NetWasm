// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.UnsafeAccessors;

internal abstract record UnsafeAccessorBinding
{
    private UnsafeAccessorBinding()
    {
    }

    internal sealed record Constructor(MethodInstanceModel Target) : UnsafeAccessorBinding;
    internal sealed record Method(MethodInstanceModel Target) : UnsafeAccessorBinding;
    internal sealed record Field(FieldInstanceModel Target) : UnsafeAccessorBinding;
    internal sealed record Failure(MethodInstanceModel ExceptionConstructor) : UnsafeAccessorBinding;
}
