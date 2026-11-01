using NetWasm.Compiler.Analysis.Loops;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

// The element sizes the loop rewrites take from a primitive's name, with
// pointer-sized elements as -1 and every other type as none.
public sealed class PrimitiveElementsTests
{
    [Theory]
    [InlineData("bool", CliValueKind.I4, 1)]
    [InlineData("i1", CliValueKind.I4, 1)]
    [InlineData("u1", CliValueKind.I4, 1)]
    [InlineData("char", CliValueKind.I4, 2)]
    [InlineData("i2", CliValueKind.I4, 2)]
    [InlineData("u2", CliValueKind.I4, 2)]
    [InlineData("i4", CliValueKind.I4, 4)]
    [InlineData("u4", CliValueKind.I4, 4)]
    [InlineData("f4", CliValueKind.F4, 4)]
    [InlineData("i8", CliValueKind.I8, 8)]
    [InlineData("u8", CliValueKind.I8, 8)]
    [InlineData("f8", CliValueKind.F8, 8)]
    [InlineData("nativeint", CliValueKind.NativeInt, -1)]
    [InlineData("nativeuint", CliValueKind.NativeInt, -1)]
    public void SizesEveryPrimitiveElement(string name, CliValueKind kind, int size)
    {
        var type = CliTypeIdentity.Primitive(name, kind);

        Assert.Equal(size, PrimitiveElements.Size(type));
        Assert.Equal(kind is CliValueKind.I4 or CliValueKind.I8, PrimitiveElements.IsInteger(type));
    }

    [Fact]
    public void AnotherTypeHasNoElementSize()
    {
        var type = CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, isValueType: false);

        Assert.Null(PrimitiveElements.Size(type));
        Assert.False(PrimitiveElements.IsInteger(type));
    }
}
