using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests;

public sealed class TestOptimizationTests
{
    [Theory]
    [InlineData(null, FinalWasmOptimization.Oz)]
    [InlineData("", FinalWasmOptimization.Oz)]
    [InlineData("O3", FinalWasmOptimization.O3)]
    [InlineData("None", FinalWasmOptimization.None)]
    public void ParsesThePresetsName(string? value, FinalWasmOptimization expected) =>
        Assert.Equal(expected, TestOptimization.Parse(value));

    [Theory]
    [InlineData("o3")]
    [InlineData("3")]
    [InlineData("Fast")]
    public void RejectsWhatIsNotAPresetsName(string value) =>
        Assert.Contains(TestOptimization.EnvironmentVariable,
            Assert.Throws<InvalidOperationException>(() => TestOptimization.Parse(value)).Message);
}
