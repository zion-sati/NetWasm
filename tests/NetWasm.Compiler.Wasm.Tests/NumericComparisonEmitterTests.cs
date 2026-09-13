using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class NumericComparisonEmitterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, CliValueKind.NativeInt, "i32")]
    [InlineData(WasmTarget.Wasm64, CliValueKind.NativeInt, "i64")]
    [InlineData(WasmTarget.Wasm32, CliValueKind.I4, "i32")]
    [InlineData(WasmTarget.Wasm32, CliValueKind.I8, "i64")]
    [InlineData(WasmTarget.Wasm32, CliValueKind.F4, "f32")]
    [InlineData(WasmTarget.Wasm32, CliValueKind.F8, "f64")]
    public void SelectsTheTargetWidthOperation(
        WasmTarget target,
        CliValueKind type,
        string expected)
    {
        var selected = "";

        NumericComparisonEmitter.Emit(
            WasmTargetLayout.For(target),
            type,
            () => selected = "i32",
            () => selected = "i64",
            () => selected = "f32",
            () => selected = "f64");

        Assert.Equal(expected, selected);
    }

    [Fact]
    public void RejectsNonNumericTypes()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            NumericComparisonEmitter.Emit(
                WasmTargetLayout.Wasm32,
                CliValueKind.ManagedReference,
                () => { },
                () => { },
                () => { },
                () => { }));

        Assert.Contains("ManagedReference", error.Message, StringComparison.Ordinal);
    }
}
