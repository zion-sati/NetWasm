using System;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class LinkedMemoryLayoutCalculator(ILinkedMemoryLayoutValidator validator) : ILinkedMemoryLayoutCalculator
{
    private readonly ILinkedMemoryLayoutValidator _validator = validator ?? throw new ArgumentNullException(nameof(validator));

    public RuntimeLinkedMemoryLayout Calculate(RuntimeMemoryPlan plan, RuntimeLinkedMemoryLayout observed)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(observed);
        if (plan.InitialHeapSizeBytes < 0 || plan.WasmPageSize != 65_536)
            throw new InvalidOperationException("The requested NetWasm initial heap size is invalid.");
        _validator.Validate(plan, observed);
        try
        {
            var end = checked(observed.HeapBase + plan.InitialHeapSizeBytes);
            var initial = checked((end + plan.WasmPageSize - 1) / plan.WasmPageSize * plan.WasmPageSize);
            if (initial > plan.MaximumMemorySizeBytes)
                throw new InvalidOperationException("The requested NetWasm maximum memory is smaller than the native-linked initial layout.");
            return observed with { InitialMemorySizeBytes = initial };
        }
        catch (OverflowException exception)
        {
            throw new InvalidOperationException("The requested NetWasm native-linked memory layout is too large.", exception);
        }
    }
}
