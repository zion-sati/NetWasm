// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Runtime.CompilerServices;

namespace NetWasm.Compiler.Tests;

public sealed class EnumMetadataViewTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(9, true)]
    public unsafe void ReadsSelectedNativeWidthStorageWithoutAllocatingArrays(int kind, bool flags)
    {
        var first = "Low";
        var second = "High";
        ulong* values = stackalloc ulong[] { 1, 1UL << 63 };
        nint* names = stackalloc nint[]
        {
            Unsafe.As<string, nint>(ref first), Unsafe.As<string, nint>(ref second),
        };
        byte* descriptor = stackalloc byte[16 + (2 * sizeof(nint))];
        Write(descriptor, 1, kind, flags ? 7 : 6, 2, (nint)values, (nint)names);

        var view = new EnumMetadataView((nint)descriptor);

        Assert.Equal(kind, view.UnderlyingTypeCode);
        Assert.Equal(flags, view.HasFlagsAttribute);
        Assert.Equal(2, view.Values.Length);
        Assert.Equal(1UL, view.Values[0]);
        Assert.Equal(1UL << 63, view.Values[1]);
        Assert.Equal(2, view.Names.Length);
        Assert.Same(first, view.Names[0]);
        Assert.Same(second, view.Names[1]);
        GC.KeepAlive(first);
        GC.KeepAlive(second);
    }

    [Fact]
    public unsafe void EmptyRequestedPayloadsMayUseNullArrayPointers()
    {
        byte* descriptor = stackalloc byte[16 + (2 * sizeof(nint))];
        Write(descriptor, 1, 5, 6, 0, 0, 0);

        var view = new EnumMetadataView((nint)descriptor);

        Assert.True(view.Values.IsEmpty);
        Assert.True(view.Names.IsEmpty);
    }

    [Theory]
    [InlineData(0, 5, 0)]
    [InlineData(2, 5, 0)]
    [InlineData(1, 0, 0)]
    [InlineData(1, 10, 0)]
    [InlineData(1, 5, -1)]
    public unsafe void RejectsInvalidDescriptorHeaders(int version, int kind, int count)
    {
        byte* descriptor = stackalloc byte[16 + (2 * sizeof(nint))];
        Write(descriptor, version, kind, 6, count, 0, 0);
        var address = (nint)descriptor;

        Assert.Throws<InvalidOperationException>(() => new EnumMetadataView(address));
    }

    [Fact]
    public void RejectsMissingDescriptor()
    {
        Assert.Throws<InvalidOperationException>(() => new EnumMetadataView(0));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(6, 1)]
    public unsafe void RejectsUnrequestedOrMissingPayloads(int flags, int count)
    {
        byte* descriptor = stackalloc byte[16 + (2 * sizeof(nint))];
        Write(descriptor, 1, 5, flags, count, 0, 0);
        var view = new EnumMetadataView((nint)descriptor);

        Assert.Throws<InvalidOperationException>(() => view.Values.Length);
        Assert.Throws<InvalidOperationException>(() => view.Names.Length);
    }

    private static unsafe void Write(
        byte* descriptor, int version, int kind, int flags, int count, nint values, nint names)
    {
        ((int*)descriptor)[0] = version;
        ((int*)descriptor)[1] = kind;
        ((int*)descriptor)[2] = flags;
        ((int*)descriptor)[3] = count;
        *(nint*)(descriptor + 16) = values;
        *(nint*)(descriptor + 16 + sizeof(nint)) = names;
    }
}
