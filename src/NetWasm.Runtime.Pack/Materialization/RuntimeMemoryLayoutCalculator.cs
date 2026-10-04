using System;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class RuntimeMemoryLayoutCalculator(IRuntimeMemoryPlanBuilder plans) : IRuntimeMemoryLayoutCalculator
{
    private readonly IRuntimeMemoryPlanBuilder _plans = plans ?? throw new ArgumentNullException(nameof(plans));

    public RuntimeMemoryLayout Calculate(RuntimeMemoryLayoutRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Target);
        var plan = _plans.Build(request);
        if (request.Target.RuntimeFootprintBytes < 0)
            throw new InvalidOperationException("The NetWasm runtime footprint is invalid.");

        try
        {
            var runtimeGlobalBase = plan.RuntimeGlobalBase;
            var heapBase = checked(runtimeGlobalBase + request.Target.RuntimeFootprintBytes);
            var initialMemorySize = Align(checked(heapBase + plan.InitialHeapSizeBytes), plan.WasmPageSize);
            if (initialMemorySize > plan.MaximumMemorySizeBytes)
            {
                throw new InvalidOperationException("The requested NetWasm maximum memory is smaller than the initial layout.");
            }

            return new RuntimeMemoryLayout(
                runtimeGlobalBase,
                heapBase,
                initialMemorySize,
                plan.MaximumMemorySizeBytes);
        }
        catch (OverflowException exception)
        {
            throw new InvalidOperationException("The requested NetWasm memory layout is too large.", exception);
        }
    }

    private static long Align(long value, long alignment) =>
        checked((value + alignment - 1) / alignment * alignment);
}
