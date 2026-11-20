// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;

namespace NetWasm.Compiler.Core.NativeInterop;

// A P/Invoke's [MarshalAs] native types (ECMA-335 II.23.4): the result's and each parameter's in declaration order, or
// null where none is given. Only a descriptor that is one native type is recorded; any other makes the declaration one
// NetWasm cannot express (NativeImportDeclaration.HasMarshalling). Compared by value, as NativeParameterFlows is.
public sealed record NativeMarshalAsTypes(UnmanagedType? Result, ImmutableArray<UnmanagedType?> Parameters)
{
    public static NativeMarshalAsTypes None { get; } = new(null, ImmutableArray<UnmanagedType?>.Empty);

    public UnmanagedType? this[int parameter] =>
        !Parameters.IsDefault && parameter < Parameters.Length ? Parameters[parameter] : null;

    public bool Equals(NativeMarshalAsTypes? other) =>
        other is not null && Result == other.Result && Parameters.AsSpan().SequenceEqual(other.Parameters.AsSpan());

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Result);
        foreach (var value in Parameters.AsSpan())
            hash.Add(value);
        return hash.ToHashCode();
    }

    // The native types a marshalling stub converts to, or that restate a blittable type, each one byte long in metadata.
    public static bool IsExpressible(UnmanagedType type) => type is
        UnmanagedType.Bool or UnmanagedType.I1 or UnmanagedType.U1 or UnmanagedType.I2 or UnmanagedType.U2 or
        UnmanagedType.I4 or UnmanagedType.U4 or UnmanagedType.I8 or UnmanagedType.U8 or UnmanagedType.R4 or
        UnmanagedType.R8 or UnmanagedType.SysInt or UnmanagedType.SysUInt or UnmanagedType.VariantBool or
        UnmanagedType.LPStr or UnmanagedType.LPWStr or UnmanagedType.LPUTF8Str;
}
