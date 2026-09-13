using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class NumericStackTypesTests
{
    [Fact]
    public void NativeIntAndInt32PromoteToNativeInt()
    {
        Assert.Equal(
            CliValueKind.NativeInt,
            NumericStackTypes.GetCompatible(
                CliValueKind.NativeInt,
                CliValueKind.I4));
        Assert.Equal(
            CliValueKind.NativeInt,
            NumericStackTypes.GetCompatible(
                CliValueKind.I4,
                CliValueKind.NativeInt));
    }

    [Theory]
    [InlineData(CliValueKind.I4)]
    [InlineData(CliValueKind.I8)]
    [InlineData(CliValueKind.NativeInt)]
    [InlineData(CliValueKind.F4)]
    [InlineData(CliValueKind.F8)]
    public void MatchingNumericKindsRemainCompatible(CliValueKind kind)
    {
        Assert.Equal(kind, NumericStackTypes.GetCompatible(kind, kind));
        Assert.Equal(kind, NumericStackTypes.RequireMatching([kind, kind], 0));
    }

    [Fact]
    public void MatchingNonNumericKindsAreRejected()
    {
        Assert.Throws<InvalidOperationException>(() =>
            NumericStackTypes.GetCompatible(
                CliValueKind.ManagedReference,
                CliValueKind.ManagedReference));
    }

    [Fact]
    public void MixedIntegerAndFloatFailDeterministically()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            NumericStackTypes.RequireMatching(
                [CliValueKind.I4, CliValueKind.F4],
                0));

        Assert.Equal(
            "numeric operation has incompatible stack types",
            exception.Message);
    }
}
