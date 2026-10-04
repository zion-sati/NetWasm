using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimeMemoryPlanBuilderTests
{
    [Theory]
    [InlineData("wasm32", 4, 2_147_483_648L)]
    [InlineData("wasm64", 8, 8_589_934_592L)]
    public void PlansDefaultsWithoutPredictingRuntimeFootprint(string target, int pointerSize, long maximum)
    {
        var builder = Assert.IsAssignableFrom<IRuntimeMemoryPlanBuilder>(new RuntimeMemoryPlanBuilder());
        var request = Request(target);
        var plan = builder.Build(request);

        Assert.Equal(new RuntimeMemoryPlan(target, pointerSize, 65_536, 16, 65_537, 65_552,
            65_536, maximum, 65_536), plan);
        Assert.Equal(plan, builder.Build(request with
        {
            Target = request.Target with { RuntimeFootprintBytes = long.MaxValue },
        }));
    }

    [Theory]
    [InlineData("wasm32")]
    [InlineData("wasm64")]
    public void PreservesArbitraryNonnegativeRequestedHeapSizes(string target)
    {
        var request = Request(target) with { InitialHeapSizeBytes = 65_537, MaximumMemorySizeBytes = 1_048_576 };
        var plan = new RuntimeMemoryPlanBuilder().Build(request);
        Assert.Equal(65_537, plan.InitialHeapSizeBytes);
        Assert.Equal(1_048_576, plan.MaximumMemorySizeBytes);
        Assert.Equal(0, new RuntimeMemoryPlanBuilder().Build(request with { InitialHeapSizeBytes = 0 }).InitialHeapSizeBytes);
        Assert.Equal(65_536, new RuntimeMemoryPlanBuilder().Build(request with { ApplicationStaticDataEnd = 65_536 }).RuntimeGlobalBase);
    }

    [Fact]
    public void SupportsTheEntireWasm32AddressSpaceWhenTheTargetAllowsIt()
    {
        var request = Request("wasm32");
        var plan = new RuntimeMemoryPlanBuilder().Build(request with
        {
            Target = request.Target with { MaximumMemorySizeBytes = 4_294_967_296 },
            MaximumMemorySizeBytes = 4_294_967_296,
        });
        Assert.Equal(4_294_967_296, plan.MaximumMemorySizeBytes);
    }

    [Theory]
    [InlineData("unknown-target")]
    [InlineData("pointer-size")]
    [InlineData("negative-application")]
    [InlineData("page-size")]
    [InlineData("zero-alignment")]
    [InlineData("non-power-alignment")]
    [InlineData("negative-stack")]
    [InlineData("unaligned-stack")]
    [InlineData("negative-heap")]
    [InlineData("zero-maximum")]
    [InlineData("beyond-target-maximum")]
    [InlineData("unaligned-maximum")]
    [InlineData("stack-beyond-maximum")]
    [InlineData("application-beyond-maximum")]
    [InlineData("wasm32-address-overflow")]
    public void RejectsInvalidRequestPolicy(string invalid)
    {
        var request = Request("wasm32");
        request = invalid switch
        {
            "unknown-target" => request with { Target = request.Target with { Target = "unknown" } },
            "pointer-size" => request with { Target = request.Target with { PointerSizeBytes = 8 } },
            "negative-application" => request with { ApplicationStaticDataEnd = -1 },
            "page-size" => request with { WasmPageSize = 1 },
            "zero-alignment" => request with { Target = request.Target with { Alignment = 0 } },
            "non-power-alignment" => request with { Target = request.Target with { Alignment = 3 } },
            "negative-stack" => request with { Target = request.Target with { NativeStackSizeBytes = -1 } },
            "unaligned-stack" => request with { Target = request.Target with { NativeStackSizeBytes = 1 } },
            "negative-heap" => request with { InitialHeapSizeBytes = -1 },
            "zero-maximum" => request with { MaximumMemorySizeBytes = 0 },
            "beyond-target-maximum" => request with { MaximumMemorySizeBytes = 2_147_549_184 },
            "unaligned-maximum" => request with { MaximumMemorySizeBytes = 65_537 },
            "stack-beyond-maximum" => request with { Target = request.Target with { NativeStackSizeBytes = 131_072 }, MaximumMemorySizeBytes = 65_536 },
            "application-beyond-maximum" => request with { ApplicationStaticDataEnd = 65_537, MaximumMemorySizeBytes = 65_536 },
            "wasm32-address-overflow" => request with { Target = request.Target with { MaximumMemorySizeBytes = 8_589_934_592 }, MaximumMemorySizeBytes = 8_589_934_592 },
            _ => throw new ArgumentOutOfRangeException(nameof(invalid)),
        };
        Assert.Throws<InvalidOperationException>(() => new RuntimeMemoryPlanBuilder().Build(request));
    }

    [Fact]
    public void RejectsAlignmentOverflowWithItsCause()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new RuntimeMemoryPlanBuilder().Build(
            Request("wasm64") with { ApplicationStaticDataEnd = long.MaxValue }));
        Assert.IsType<OverflowException>(exception.InnerException);
    }

    [Fact]
    public void RejectsNullInputs()
    {
        var builder = new RuntimeMemoryPlanBuilder();
        Assert.Throws<ArgumentNullException>(() => builder.Build(null!));
        Assert.Throws<ArgumentNullException>(() => builder.Build(Request("wasm32") with { Target = null! }));
    }

    private static RuntimeMemoryLayoutRequest Request(string target) =>
        new(RuntimePackTestData.Target(target), 65_536, 65_537, null, null);
}
