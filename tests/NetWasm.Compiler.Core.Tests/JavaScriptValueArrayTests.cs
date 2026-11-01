using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Core.Tests;

public sealed class JavaScriptValueArrayTests
{
    [Theory]
    [InlineData("u1", "bytes", "System.Byte")]
    [InlineData("i1", "i8[]", "System.SByte")]
    [InlineData("i2", "i16[]", "System.Int16")]
    [InlineData("u2", "u16[]", "System.UInt16")]
    [InlineData("i4", "i32[]", "System.Int32")]
    [InlineData("u4", "u32[]", "System.UInt32")]
    [InlineData("i8", "i64[]", "System.Int64")]
    [InlineData("u8", "u64[]", "System.UInt64")]
    [InlineData("f4", "f32[]", "System.Single")]
    [InlineData("f8", "f64[]", "System.Double")]
    public void NamesEveryPrimitiveNumberArray(string element, string abiType, string elementTypeName)
    {
        var array = CliTypeIdentity.SzArray(CliTypeIdentity.Primitive(element, CliValueKind.I4));

        Assert.True(JavaScriptValueArray.IsSupported(array));
        Assert.Equal(abiType, JavaScriptValueArray.GetAbiType(array));
        Assert.Equal(elementTypeName, JavaScriptValueArray.GetElementTypeName(array));
    }

    [Fact]
    public void RejectsEveryOtherShape()
    {
        var number = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        foreach (var type in new[]
                 {
                     number,
                     CliTypeIdentity.SzArray(CliTypeIdentity.Primitive("bool", CliValueKind.I4)),
                     CliTypeIdentity.SzArray(CliTypeIdentity.Primitive("char", CliValueKind.I4)),
                     CliTypeIdentity.SzArray(CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference)),
                     CliTypeIdentity.Array(number, 2),
                 })
        {
            Assert.False(JavaScriptValueArray.IsSupported(type));
            Assert.Throws<ArgumentException>(() => JavaScriptValueArray.GetAbiType(type));
            Assert.Throws<ArgumentException>(() => JavaScriptValueArray.GetElementTypeName(type));
        }
        Assert.Throws<ArgumentNullException>(() => JavaScriptValueArray.IsSupported(null!));
    }
}
