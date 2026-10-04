using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimeMemoryLayoutCalculatorTests
{
    [Theory]
    [InlineData("wasm32", 65_537, 65_552, 157_520, 262_144, 2_147_483_648)]
    [InlineData("wasm64", 65_537, 65_552, 179_904, 262_144, 8_589_934_592)]
    public void CalculatesTargetSpecificPerApplicationLayout(
        string target,
        long staticDataEnd,
        long expectedRuntimeBase,
        long expectedHeapBase,
        long expectedInitialMemory,
        long expectedMaximumMemory)
    {
        var calculator = Assert.IsAssignableFrom<IRuntimeMemoryLayoutCalculator>(
            new RuntimeMemoryLayoutCalculator(new RuntimeMemoryPlanBuilder()));
        var result = calculator.Calculate(new(
            RuntimePackTestData.Target(target),
            65_536,
            staticDataEnd,
            null,
            null));

        Assert.Equal(expectedRuntimeBase, result.RuntimeGlobalBase);
        Assert.Equal(expectedHeapBase, result.HeapBase);
        Assert.Equal(expectedInitialMemory, result.InitialMemorySizeBytes);
        Assert.Equal(expectedMaximumMemory, result.MaximumMemorySizeBytes);
    }

    [Fact]
    public void AppliesExplicitHeapAndMaximumLimits()
    {
        var result = new RuntimeMemoryLayoutCalculator(new RuntimeMemoryPlanBuilder()).Calculate(new(
            RuntimePackTestData.Target("wasm32"),
            65_536,
            16,
            0,
            1_048_576));

        Assert.Equal(16, result.RuntimeGlobalBase);
        Assert.Equal(91_984, result.HeapBase);
        Assert.Equal(131_072, result.InitialMemorySizeBytes);
        Assert.Equal(1_048_576, result.MaximumMemorySizeBytes);
    }

    [Theory]
    [InlineData(-1L, 65_536L, null, null)]
    [InlineData(0L, 0L, null, null)]
    [InlineData(0L, 65_536L, -1L, null)]
    [InlineData(0L, 65_536L, null, 0L)]
    [InlineData(0L, 65_536L, null, 65_537L)]
    [InlineData(0L, 65_536L, null, 2_147_549_184L)]
    public void RejectsInvalidRequests(
        long staticDataEnd,
        long pageSize,
        long? initialHeap,
        long? maximumMemory)
    {
        Assert.Throws<InvalidOperationException>(() => new RuntimeMemoryLayoutCalculator(new RuntimeMemoryPlanBuilder()).Calculate(new(
            RuntimePackTestData.Target("wasm32"),
            pageSize,
            staticDataEnd,
            initialHeap,
            maximumMemory)));
    }

    [Fact]
    public void RejectsMaximumBelowInitialLayout() =>
        Assert.Throws<InvalidOperationException>(() => new RuntimeMemoryLayoutCalculator(new RuntimeMemoryPlanBuilder()).Calculate(new(
            RuntimePackTestData.Target("wasm32"),
            65_536,
            1_000_000,
            65_536,
            1_048_576)));

    [Fact]
    public void RejectsOverflowingLayout() =>
        Assert.Throws<InvalidOperationException>(() => new RuntimeMemoryLayoutCalculator(new RuntimeMemoryPlanBuilder()).Calculate(new(
            RuntimePackTestData.Target("wasm64") with { MaximumMemorySizeBytes = long.MaxValue },
            65_536,
            long.MaxValue,
            0,
            long.MaxValue - 65_535)));

    [Fact]
    public void RejectsNullRequest()
    {
        var calculator = new RuntimeMemoryLayoutCalculator(new RuntimeMemoryPlanBuilder());
        Assert.Throws<ArgumentNullException>(() => calculator.Calculate(null!));
        Assert.Throws<ArgumentNullException>(() => calculator.Calculate(new(
            null!, 65_536, 0, null, null)));
    }

    [Fact]
    public void UsesInjectedPlanAndPropagatesItsFailure()
    {
        var request = new RuntimeMemoryLayoutRequest(RuntimePackTestData.Target("wasm32"), 65_536, 0, null, null);
        var plan = new RuntimeMemoryPlan("wasm32", 4, 65_536, 16, 0, 16, 0, 1_048_576, 65_536);
        var builder = new RecordingPlanBuilder(plan);
        var result = new RuntimeMemoryLayoutCalculator(builder).Calculate(request);
        Assert.Same(request, builder.Request);
        Assert.Equal(new RuntimeMemoryLayout(16, 91_984, 131_072, 1_048_576), result);
        var failure = new InvalidOperationException("plan");
        builder.Failure = failure;
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => new RuntimeMemoryLayoutCalculator(builder).Calculate(request)));
    }

    [Fact]
    public void RejectsNegativeFootprintAndOverflowingFootprintCalculation()
    {
        var plan = new RuntimeMemoryPlan("wasm64", 8, 65_536, 16, 0, 16, 0, long.MaxValue, 65_536);
        var builder = new RecordingPlanBuilder(plan);
        var request = new RuntimeMemoryLayoutRequest(RuntimePackTestData.Target("wasm64"), 65_536, 0, null, null);
        Assert.Throws<InvalidOperationException>(() => new RuntimeMemoryLayoutCalculator(builder).Calculate(request with
        {
            Target = request.Target with { RuntimeFootprintBytes = -1 },
        }));
        var failure = Assert.Throws<InvalidOperationException>(() => new RuntimeMemoryLayoutCalculator(builder).Calculate(request with
        {
            Target = request.Target with { RuntimeFootprintBytes = long.MaxValue },
        }));
        Assert.IsType<OverflowException>(failure.InnerException);
        Assert.Throws<ArgumentNullException>(() => new RuntimeMemoryLayoutCalculator(null!));
    }

    private sealed class RecordingPlanBuilder(RuntimeMemoryPlan plan) : IRuntimeMemoryPlanBuilder
    {
        public RuntimeMemoryLayoutRequest? Request { get; private set; }
        public InvalidOperationException? Failure { get; set; }

        public RuntimeMemoryPlan Build(RuntimeMemoryLayoutRequest request)
        {
            Request = request;
            if (Failure is not null)
                throw Failure;
            return plan;
        }
    }
}
