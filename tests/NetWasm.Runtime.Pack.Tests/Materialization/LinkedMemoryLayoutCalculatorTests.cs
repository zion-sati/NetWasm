using NetWasm.Runtime.Pack.Materialization;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class LinkedMemoryLayoutCalculatorTests
{
    [Theory]
    [InlineData("wasm32", 4, 0L, 132_128L, 1_048_576L, 196_608L)]
    [InlineData("wasm32", 4, 65_537L, 132_128L, 1_048_576L, 262_144L)]
    [InlineData("wasm64", 8, 65_537L, 4_294_967_312L, 8_589_934_592L, 4_295_098_368L)]
    public void CalculatesFromObservedHeapWithoutRoundingRequestedHeapFirst(
        string target, int pointerSize, long requestedHeap, long heapBase, long maximum, long expectedInitial)
    {
        var validator = new RecordingValidator();
        var calculator = Assert.IsAssignableFrom<ILinkedMemoryLayoutCalculator>(new LinkedMemoryLayoutCalculator(validator));
        var plan = Plan(target, pointerSize) with { InitialHeapSizeBytes = requestedHeap, MaximumMemorySizeBytes = maximum };
        var observed = Observed(target) with
        {
            HeapBase = heapBase,
            InitialMemorySizeBytes = (heapBase + 65_535) / 65_536 * 65_536,
            MaximumMemorySizeBytes = maximum,
        };

        var actual = calculator.Calculate(plan, observed);

        Assert.Equal(observed with { InitialMemorySizeBytes = expectedInitial }, actual);
        Assert.Equal((plan, observed), Assert.Single(validator.Calls));
        Assert.Equal(0, validator.ExactCalls);
    }

    [Fact]
    public void RejectsInsufficientMaximum()
    {
        var validator = new RecordingValidator();
        var plan = Plan("wasm32", 4) with { MaximumMemorySizeBytes = 65_536 };
        Assert.Throws<InvalidOperationException>(() => new LinkedMemoryLayoutCalculator(validator).Calculate(plan, Observed("wasm32")));
        Assert.Single(validator.Calls);
    }

    [Fact]
    public void PreservesValidationFailureWithoutCalculating()
    {
        var failure = new InvalidOperationException("bounds");
        var validator = new RecordingValidator { Failure = failure };
        var actual = Assert.Throws<InvalidOperationException>(() => new LinkedMemoryLayoutCalculator(validator)
            .Calculate(Plan("wasm32", 4), Observed("wasm32")));
        Assert.Same(failure, actual);
    }

    [Theory]
    [InlineData(long.MaxValue, 1)]
    [InlineData(long.MaxValue, 0)]
    public void RejectsHeapAdditionAndPageAlignmentOverflow(long heapBase, long requestedHeap)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new LinkedMemoryLayoutCalculator(new RecordingValidator())
            .Calculate(Plan("wasm64", 8) with { InitialHeapSizeBytes = requestedHeap, MaximumMemorySizeBytes = long.MaxValue },
                Observed("wasm64") with { HeapBase = heapBase }));
        Assert.IsType<OverflowException>(exception.InnerException);
    }

    [Fact]
    public void RejectsInvalidAndNullInputsBeforeCollaborators()
    {
        Assert.Throws<ArgumentNullException>(() => new LinkedMemoryLayoutCalculator(null!));
        var validator = new RecordingValidator();
        var calculator = new LinkedMemoryLayoutCalculator(validator);
        var plan = Plan("wasm32", 4);
        var observed = Observed("wasm32");
        Assert.Throws<ArgumentNullException>(() => calculator.Calculate(null!, observed));
        Assert.Throws<ArgumentNullException>(() => calculator.Calculate(plan, null!));
        Assert.Throws<InvalidOperationException>(() => calculator.Calculate(plan with { InitialHeapSizeBytes = -1 }, observed));
        Assert.Throws<InvalidOperationException>(() => calculator.Calculate(plan with { WasmPageSize = 0 }, observed));
        Assert.Empty(validator.Calls);
    }

    private static RuntimeMemoryPlan Plan(string target, int pointerSize) =>
        new(target, pointerSize, 65_536, 16, 65_537, 65_552, 65_537, 1_048_576, 65_536);

    private static RuntimeLinkedMemoryLayout Observed(string target) =>
        new(target, 65_552, 66_576, 66_576, 132_112, 132_128, 196_608, 1_048_576);

    private sealed class RecordingValidator : ILinkedMemoryLayoutValidator
    {
        public List<(RuntimeMemoryPlan Plan, RuntimeLinkedMemoryLayout Observed)> Calls { get; } = [];
        public InvalidOperationException? Failure { get; init; }
        public int ExactCalls { get; private set; }

        public void Validate(RuntimeMemoryPlan plan, RuntimeLinkedMemoryLayout observed)
        {
            Calls.Add((plan, observed));
            if (Failure is not null)
                throw Failure;
        }

        public void Validate(RuntimeMemoryPlan plan, RuntimeLinkedMemoryLayout observed, RuntimeLinkedMemoryLayout expected) =>
            ExactCalls++;
    }
}
