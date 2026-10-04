using System;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class LinkedMemoryLayoutValidator : ILinkedMemoryLayoutValidator
{
    public void Validate(RuntimeMemoryPlan plan, RuntimeLinkedMemoryLayout observed)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(observed);
        if (plan.Alignment <= 0 || plan.WasmPageSize != 65_536 ||
            !string.Equals(observed.Target, plan.Target, StringComparison.Ordinal) ||
            observed.RuntimeGlobalBase != plan.RuntimeGlobalBase ||
            observed.RuntimeGlobalBase < plan.ApplicationStaticDataEnd ||
            observed.RuntimeGlobalBase < 0 || observed.RuntimeGlobalBase > observed.DataEnd ||
            observed.DataEnd > observed.StackLow || observed.StackLow > observed.StackHigh ||
            observed.StackHigh > observed.HeapBase || observed.HeapBase > observed.InitialMemorySizeBytes ||
            observed.InitialMemorySizeBytes > observed.MaximumMemorySizeBytes ||
            observed.MaximumMemorySizeBytes != plan.MaximumMemorySizeBytes ||
            observed.StackHigh - observed.StackLow != plan.NativeStackSizeBytes ||
            observed.RuntimeGlobalBase % plan.Alignment != 0 || observed.StackLow % plan.Alignment != 0 ||
            observed.StackHigh % plan.Alignment != 0 || observed.HeapBase % plan.Alignment != 0 ||
            observed.InitialMemorySizeBytes % plan.WasmPageSize != 0 ||
            observed.MaximumMemorySizeBytes % plan.WasmPageSize != 0)
            throw new InvalidOperationException("The native-linked Wasm memory bounds do not match the requested layout.");
    }

    public void Validate(
        RuntimeMemoryPlan plan, RuntimeLinkedMemoryLayout observed, RuntimeLinkedMemoryLayout expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        Validate(plan, observed);
        if (observed != expected)
            throw new InvalidOperationException("The native-linked Wasm memory bounds changed after layout calculation.");
    }
}
