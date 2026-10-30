using System.Collections.Immutable;
using NetWasm.Compiler.Analysis.Constants;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis.Constants;

public sealed class KnownSizeRewriterTests
{
    private static readonly AssemblyIdentity CoreLib = new("NetWasm.CoreLib");

    [Theory]
    [InlineData("primitive:u1", 1)]
    [InlineData("primitive:char", 2)]
    [InlineData("primitive:f4", 4)]
    [InlineData("primitive:i8", 8)]
    public void PrimitiveSizesAreTheirOwn(string name, int size) =>
        Assert.Equal(size, KnownSizeRewriter.Size(Primitive(name)));

    [Theory]
    [InlineData("System.Runtime.Intrinsics", "Vector64`1", 8)]
    [InlineData("System.Runtime.Intrinsics", "Vector128`1", 16)]
    [InlineData("System.Numerics", "Vector`1", 16)]
    [InlineData("System.Runtime.Intrinsics", "Vector256`1", 32)]
    [InlineData("System.Runtime.Intrinsics", "Vector512`1", 64)]
    public void VectorSizesAreFixed(string @namespace, string name, int size) =>
        Assert.Equal(size, KnownSizeRewriter.Size(CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(CoreLib, @namespace, name, isValueType: true), [Primitive("primitive:i4")])));

    // Pointer-sized and laid-out types keep their sizeof.
    [Fact]
    public void OtherSizesStayUnknown()
    {
        Assert.Null(KnownSizeRewriter.Size(Primitive("primitive:nativeint")));
        Assert.Null(KnownSizeRewriter.Size(CliTypeIdentity.Named(CoreLib, "Fixture", "Pair", isValueType: true)));
    }

    private static CliTypeIdentity Primitive(string canonicalName) => canonicalName switch
    {
        "primitive:u1" => CliTypeIdentity.Primitive("u1", CliValueKind.I4),
        "primitive:char" => CliTypeIdentity.Primitive("char", CliValueKind.I4),
        "primitive:f4" => CliTypeIdentity.Primitive("f4", CliValueKind.F4),
        "primitive:i8" => CliTypeIdentity.Primitive("i8", CliValueKind.I8),
        "primitive:i4" => CliTypeIdentity.Primitive("i4", CliValueKind.I4),
        _ => CliTypeIdentity.Primitive("nativeint", CliValueKind.NativeInt),
    };
}
