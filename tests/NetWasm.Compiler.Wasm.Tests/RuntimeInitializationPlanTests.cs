using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class RuntimeInitializationPlanTests
{
    [Fact]
    public void PlanRetainsValidInitializationState()
    {
        var stackTrace = StackTraceMethodPlan.Disabled;

        var plan = new RuntimeInitializationPlan(42, stackTrace);

        Assert.Equal(42, plan.StaticDataEnd);
        Assert.Same(stackTrace, plan.StackTraceMethods);
    }

    [Fact]
    public void PlanRejectsInvalidInitializationState()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RuntimeInitializationPlan(-1, StackTraceMethodPlan.Disabled));
        Assert.Throws<ArgumentNullException>(() =>
            new RuntimeInitializationPlan(0, null!));
    }
}
