using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class RectangularArrayLayoutProviderTests
{
    [Fact]
    public void RejectsMissingTargetLayout()
    {
        var layouts = new RecordingLayoutProvider();

        Assert.Throws<ArgumentNullException>(() =>
            new RectangularArrayLayoutProvider(null!, layouts));
    }

    [Fact]
    public void RejectsMissingObjectLayout()
    {
        var layouts = new RecordingLayoutProvider();

        Assert.Throws<ArgumentNullException>(() =>
            new RectangularArrayLayoutProvider(layouts, null!));
    }

    [Theory]
    [InlineData(false, 16, 20, 24)]
    [InlineData(true, 28, 32, 40)]
    public void ProvidesTargetParametricLayout(
        bool memory64,
        int rankOffset,
        int shapePointerOffset,
        int objectSize)
    {
        var layouts = new RecordingLayoutProvider(
            memory64 ? WasmTargetLayout.Wasm64 : WasmTargetLayout.Wasm32);
        var provider = ThroughContract(
            new RectangularArrayLayoutProvider(layouts, layouts));

        var result = provider.Provide();

        Assert.Equal(rankOffset, result.RankOffset);
        Assert.Equal(shapePointerOffset, result.ShapePointerOffset);
        Assert.Equal(objectSize, result.ObjectSize);
    }

    private static IRectangularArrayLayoutProvider ThroughContract(
        IRectangularArrayLayoutProvider provider) => provider;
}
