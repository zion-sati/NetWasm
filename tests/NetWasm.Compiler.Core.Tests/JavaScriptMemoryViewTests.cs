using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Core.Tests;

public sealed class JavaScriptMemoryViewTests
{
    private static readonly AssemblyIdentity Assembly = new("NetWasm.CoreLib");

    [Theory]
    [InlineData("Span`1", "u1", "u8-view")]
    [InlineData("ReadOnlySpan`1", "u1", "u8-view")]
    [InlineData("Span`1", "i1", "i8-view")]
    [InlineData("Span`1", "i2", "i16-view")]
    [InlineData("Span`1", "u2", "u16-view")]
    [InlineData("Span`1", "i4", "i32-view")]
    [InlineData("Span`1", "u4", "u32-view")]
    [InlineData("ReadOnlySpan`1", "i8", "i64-view")]
    [InlineData("Span`1", "u8", "u64-view")]
    [InlineData("Span`1", "f4", "f32-view")]
    [InlineData("ReadOnlySpan`1", "f8", "f64-view")]
    public void NamesEverySpanOfAPrimitiveNumber(string span, string element, string abiType)
    {
        var type = Span(span, CliTypeIdentity.Primitive(element, CliValueKind.I4));

        Assert.True(JavaScriptMemoryView.IsSupported(type));
        Assert.Equal(abiType, JavaScriptMemoryView.GetAbiType(type));
    }

    [Fact]
    public void RejectsEveryOtherShape()
    {
        var number = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        foreach (var type in new[]
                 {
                     number,
                     CliTypeIdentity.SzArray(number),
                     Span("Span`1", CliTypeIdentity.Primitive("bool", CliValueKind.I4)),
                     Span("Span`1", CliTypeIdentity.Primitive("char", CliValueKind.I4)),
                     Span("Memory`1", number),
                     CliTypeIdentity.GenericInstantiation(
                         CliTypeIdentity.Named(Assembly, "System", "Span`1", isValueType: true),
                         [number, number]),
                 })
        {
            Assert.False(JavaScriptMemoryView.IsSupported(type));
            Assert.Throws<ArgumentException>(() => JavaScriptMemoryView.GetAbiType(type));
        }
        Assert.Throws<ArgumentNullException>(() => JavaScriptMemoryView.IsSupported(null!));
    }

    [Fact]
    public void ListsTheViewNamesInOrdinalOrder()
    {
        Assert.Equal(
            ["f32-view", "f64-view", "i16-view", "i32-view", "i64-view", "i8-view", "u16-view", "u32-view", "u64-view", "u8-view"],
            JavaScriptMemoryView.AbiTypes.ToArray());
    }

    private static CliTypeIdentity Span(string name, CliTypeIdentity element) =>
        CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(Assembly, "System", name, isValueType: true),
            [element]);
}
