// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Reflection;
using System.Runtime.InteropServices;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

// How desktop .NET's runtime marshalling passes a bool or char on Unix, and which [MarshalAs] a primitive takes.
public sealed class NativeScalarMarshallingTests
{
    [Theory]
    [InlineData("bool", null, MethodImportAttributes.None, "i4", nameof(NativeScalarConversion.Boolean))]
    [InlineData("bool", UnmanagedType.Bool, MethodImportAttributes.None, "i4", nameof(NativeScalarConversion.Boolean))]
    [InlineData("bool", UnmanagedType.U1, MethodImportAttributes.None, "u1", nameof(NativeScalarConversion.Boolean))]
    [InlineData("bool", UnmanagedType.I1, MethodImportAttributes.None, "i1", nameof(NativeScalarConversion.Boolean))]
    [InlineData("bool", UnmanagedType.VariantBool, MethodImportAttributes.None, null, null)]
    [InlineData("char", UnmanagedType.U2, MethodImportAttributes.None, "u2", nameof(NativeScalarConversion.None))]
    [InlineData("char", UnmanagedType.I2, MethodImportAttributes.None, "i2", nameof(NativeScalarConversion.None))]
    [InlineData("char", UnmanagedType.U1, MethodImportAttributes.CharSetUnicode, "u1", nameof(NativeScalarConversion.AnsiChar))]
    [InlineData("char", UnmanagedType.I1, MethodImportAttributes.None, "i1", nameof(NativeScalarConversion.AnsiChar))]
    [InlineData("char", null, MethodImportAttributes.CharSetUnicode, "u2", nameof(NativeScalarConversion.None))]
    [InlineData("char", null, MethodImportAttributes.CharSetAnsi, "u1", nameof(NativeScalarConversion.AnsiChar))]
    [InlineData("char", null, MethodImportAttributes.CharSetAuto, "u1", nameof(NativeScalarConversion.AnsiChar))]
    [InlineData("char", UnmanagedType.I4, MethodImportAttributes.None, null, null)]
    [InlineData("i4", null, MethodImportAttributes.None, null, null)]
    public void ABoolOrCharTakesItsNativeForm(string type, UnmanagedType? marshalAs, MethodImportAttributes attributes,
        string? native, string? conversion)
    {
        var form = NativeScalarMarshalling.Form(CliTypeIdentity.Primitive(type, CliValueKind.I4), marshalAs, attributes);

        Assert.Equal((native is null ? null : $"primitive:{native}", conversion),
            (form?.Native.CanonicalName, form?.Conversion.ToString()));
        Assert.Equal(type is "bool" or "char", NativeScalarMarshalling.Converts(CliTypeIdentity.Primitive(type, CliValueKind.I4)));
    }

    // A [MarshalAs] a primitive takes restates its size (either signedness); another fails as desktop .NET's does.
    [Theory]
    [InlineData("i4", null, null)]
    [InlineData("bool", UnmanagedType.Bool, null)]
    [InlineData("bool", UnmanagedType.U1, null)]
    [InlineData("bool", UnmanagedType.I1, null)]
    [InlineData("bool", UnmanagedType.I4, "booleans must be paired")]
    [InlineData("char", UnmanagedType.U1, null)]
    [InlineData("char", UnmanagedType.I1, null)]
    [InlineData("char", UnmanagedType.U2, null)]
    [InlineData("char", UnmanagedType.I2, null)]
    [InlineData("char", UnmanagedType.I4, "chars must be paired")]
    [InlineData("i1", UnmanagedType.U1, null)]
    [InlineData("u1", UnmanagedType.I1, null)]
    [InlineData("i1", UnmanagedType.I2, "Invalid managed/unmanaged type combination.")]
    [InlineData("i2", UnmanagedType.U2, null)]
    [InlineData("u2", UnmanagedType.I4, "Invalid managed/unmanaged type combination.")]
    [InlineData("i4", UnmanagedType.U4, null)]
    [InlineData("u4", UnmanagedType.I8, "Invalid managed/unmanaged type combination.")]
    [InlineData("i8", UnmanagedType.I8, null)]
    [InlineData("u8", UnmanagedType.I4, "Invalid managed/unmanaged type combination.")]
    [InlineData("f4", UnmanagedType.R4, null)]
    [InlineData("f4", UnmanagedType.R8, "Invalid managed/unmanaged type combination.")]
    [InlineData("f8", UnmanagedType.R8, null)]
    [InlineData("f8", UnmanagedType.R4, "Invalid managed/unmanaged type combination.")]
    [InlineData("nativeint", UnmanagedType.SysInt, null)]
    [InlineData("nativeuint", UnmanagedType.SysInt, null)]
    [InlineData("nativeint", UnmanagedType.I4, "Invalid managed/unmanaged type combination.")]
    [InlineData("string", UnmanagedType.LPStr, null)]
    public void APrimitiveTakesAMarshalAsThatRestatesIt(string type, UnmanagedType? marshalAs, string? failure)
    {
        var mismatch = NativeScalarMarshalling.Mismatch(CliTypeIdentity.Primitive(type, CliValueKind.I4), marshalAs);

        if (failure is null)
            Assert.Null(mismatch);
        else
            Assert.Contains(failure, mismatch);
    }
}
