// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Runtime.InteropServices;
using NetWasm.Compiler.Core.NativeInterop;

namespace NetWasm.Compiler.Core.Tests.NativeInterop;

// A declaration's [MarshalAs] types compare by value, the result and each parameter in order, and equal ones hash alike.
public sealed class NativeMarshalAsTypesTests
{
    [Fact]
    public void TypesCompareAndHashByValue()
    {
        var types = new NativeMarshalAsTypes(UnmanagedType.I1, [UnmanagedType.Bool, null]);
        var same = new NativeMarshalAsTypes(UnmanagedType.I1, [UnmanagedType.Bool, null]);

        Assert.Equal(types, same);
        Assert.Equal(types.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(types, new NativeMarshalAsTypes(UnmanagedType.U1, [UnmanagedType.Bool, null]));
        Assert.NotEqual(types, new NativeMarshalAsTypes(UnmanagedType.I1, [UnmanagedType.I1, null]));
        Assert.False(types.Equals(null));
        Assert.Equal(NativeMarshalAsTypes.None.GetHashCode(), new NativeMarshalAsTypes(null, []).GetHashCode());
    }
}
