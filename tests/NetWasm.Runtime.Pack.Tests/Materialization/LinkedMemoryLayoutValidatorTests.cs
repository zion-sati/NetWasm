using NetWasm.Runtime.Pack.Materialization;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class LinkedMemoryLayoutValidatorTests
{
    [Theory]
    [InlineData("wasm32", 4)]
    [InlineData("wasm64", 8)]
    public void ValidatesObservedProbeAndFrozenFinalBounds(string target, int pointerSize)
    {
        var validator = Assert.IsAssignableFrom<ILinkedMemoryLayoutValidator>(new LinkedMemoryLayoutValidator());
        var plan = Plan(target, pointerSize);
        var layout = Layout(target);
        validator.Validate(plan, layout);
        validator.Validate(plan, layout, layout with { });

        // Merely-valid bounds must not replace frozen relink/optimization/cache authority.
        Assert.Throws<InvalidOperationException>(() => validator.Validate(plan,
            layout with { DataEnd = layout.DataEnd - 1 }, layout));
        Assert.Throws<InvalidOperationException>(() => validator.Validate(plan,
            layout with { InitialMemorySizeBytes = layout.InitialMemorySizeBytes + 65_536 }, layout));
    }

    [Theory]
    [InlineData("target")]
    [InlineData("global-base")]
    [InlineData("application-overlap")]
    [InlineData("negative-global-base")]
    [InlineData("data-before-base")]
    [InlineData("data-stack-overlap")]
    [InlineData("stack-order")]
    [InlineData("stack-heap-overlap")]
    [InlineData("heap-outside-memory")]
    [InlineData("initial-beyond-maximum")]
    [InlineData("maximum")]
    [InlineData("stack-size")]
    [InlineData("unaligned-global-base")]
    [InlineData("unaligned-stack-low")]
    [InlineData("unaligned-stack-high")]
    [InlineData("unaligned-heap")]
    [InlineData("unaligned-initial-memory")]
    [InlineData("unaligned-maximum-memory")]
    [InlineData("invalid-alignment")]
    [InlineData("invalid-page-size")]
    public void RejectsContradictoryAuthority(string invalid)
    {
        var plan = Plan("wasm32", 4);
        var layout = Layout("wasm32");
        (plan, layout) = invalid switch
        {
            "target" => (plan, layout with { Target = "wasm64" }),
            "global-base" => (plan, layout with { RuntimeGlobalBase = 65_536 }),
            "application-overlap" => (plan with { ApplicationStaticDataEnd = 65_553 }, layout),
            "negative-global-base" => (plan with { ApplicationStaticDataEnd = -1, RuntimeGlobalBase = -1 }, layout with { RuntimeGlobalBase = -1 }),
            "data-before-base" => (plan, layout with { DataEnd = 65_551 }),
            "data-stack-overlap" => (plan, layout with { DataEnd = 70_000 }),
            "stack-order" => (plan, layout with { StackHigh = layout.StackLow - 1 }),
            "stack-heap-overlap" => (plan, layout with { HeapBase = layout.StackHigh - 1 }),
            "heap-outside-memory" => (plan, layout with { InitialMemorySizeBytes = 65_536 }),
            "initial-beyond-maximum" => (plan, layout with { InitialMemorySizeBytes = plan.MaximumMemorySizeBytes + 65_536 }),
            "maximum" => (plan, layout with { MaximumMemorySizeBytes = 524_288 }),
            "stack-size" => (plan with { NativeStackSizeBytes = 32_768 }, layout),
            "unaligned-global-base" => (plan with { RuntimeGlobalBase = 65_553 }, layout with { RuntimeGlobalBase = 65_553 }),
            "unaligned-stack-low" => (plan, layout with { StackLow = layout.StackLow + 1, StackHigh = layout.StackHigh + 1 }),
            "unaligned-stack-high" => (plan with { NativeStackSizeBytes = 65_537 }, layout with { StackHigh = layout.StackHigh + 1 }),
            "unaligned-heap" => (plan, layout with { HeapBase = layout.HeapBase + 1 }),
            "unaligned-initial-memory" => (plan, layout with { InitialMemorySizeBytes = layout.InitialMemorySizeBytes + 1 }),
            "unaligned-maximum-memory" => (plan with { MaximumMemorySizeBytes = 1_048_577 }, layout with { MaximumMemorySizeBytes = 1_048_577 }),
            "invalid-alignment" => (plan with { Alignment = 0 }, layout),
            "invalid-page-size" => (plan with { WasmPageSize = 0 }, layout),
            _ => throw new ArgumentOutOfRangeException(nameof(invalid)),
        };
        Assert.Throws<InvalidOperationException>(() => new LinkedMemoryLayoutValidator().Validate(plan, layout));
    }

    [Fact]
    public void RejectsNullInputs()
    {
        var validator = new LinkedMemoryLayoutValidator();
        var plan = Plan("wasm32", 4);
        var layout = Layout("wasm32");
        Assert.Throws<ArgumentNullException>(() => validator.Validate(null!, layout));
        Assert.Throws<ArgumentNullException>(() => validator.Validate(plan, null!));
        Assert.Throws<ArgumentNullException>(() => validator.Validate(plan, layout, null!));
    }

    private static RuntimeMemoryPlan Plan(string target, int pointerSize) =>
        new(target, pointerSize, 65_536, 16, 65_537, 65_552, 65_537, 1_048_576, 65_536);

    private static RuntimeLinkedMemoryLayout Layout(string target) =>
        new(target, 65_552, 66_576, 66_576, 132_112, 132_128, 196_608, 1_048_576);
}
