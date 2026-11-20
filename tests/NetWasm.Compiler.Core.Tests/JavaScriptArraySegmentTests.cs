// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Core.Tests;

public sealed class JavaScriptArraySegmentTests
{
    private static readonly AssemblyIdentity Assembly = new("NetWasm.CoreLib");

    [Theory]
    [InlineData("u1", "ByteSegment")]
    [InlineData("i1", "SByteSegment")]
    [InlineData("i2", "Int16Segment")]
    [InlineData("u2", "UInt16Segment")]
    [InlineData("i4", "Int32Segment")]
    [InlineData("u4", "UInt32Segment")]
    [InlineData("i8", "Int64Segment")]
    [InlineData("u8", "UInt64Segment")]
    [InlineData("f4", "SingleSegment")]
    [InlineData("f8", "DoubleSegment")]
    public void PassesEverySegmentOfAPrimitiveNumberAsAMemoryView(string element, string suffix)
    {
        var type = Segment("ArraySegment`1", CliTypeIdentity.Primitive(element, CliValueKind.I4));

        Assert.True(JavaScriptArraySegment.IsSupported(type));
        Assert.Equal(suffix, JavaScriptArraySegment.ConversionSuffix(type));
        Assert.Equal(JavaScriptArraySegment.AbiType, JavaScriptConversion.HandleAbiType(type));
        Assert.True(JavaScriptConversion.IsHandleValue(type));
        Assert.Equal("From" + suffix, JavaScriptConversion.FromManaged(type));
        Assert.Null(JavaScriptMarshalAsRules.Mismatch(type, "MemoryView", _ => null));
        Assert.Equal("MemoryView", JavaScriptMarshalAsRules.Mismatch(type, "Array<Number>", _ => null));
    }

    [Fact]
    public void RejectsEveryOtherShape()
    {
        var number = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        foreach (var type in new[]
                 {
                     number,
                     CliTypeIdentity.SzArray(number),
                     Segment("ArraySegment`1", CliTypeIdentity.Primitive("bool", CliValueKind.I4)),
                     Segment("ArraySegment`1", CliTypeIdentity.Primitive("char", CliValueKind.I4)),
                     Segment("Span`1", number),
                     CliTypeIdentity.GenericInstantiation(
                         CliTypeIdentity.Named(Assembly, "System", "ArraySegment`1", isValueType: true),
                         [number, number]),
                 })
        {
            Assert.False(JavaScriptArraySegment.IsSupported(type));
            Assert.Throws<ArgumentException>(() => JavaScriptArraySegment.ConversionSuffix(type));
        }
        Assert.Throws<ArgumentNullException>(() => JavaScriptArraySegment.IsSupported(null!));
    }

    private static CliTypeIdentity Segment(string name, CliTypeIdentity element) =>
        CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(Assembly, "System", name, isValueType: true),
            [element]);
}
