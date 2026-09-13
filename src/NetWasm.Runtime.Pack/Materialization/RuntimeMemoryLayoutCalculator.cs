using System;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class RuntimeMemoryLayoutCalculator : IRuntimeMemoryLayoutCalculator
{
    public RuntimeMemoryLayout Calculate(RuntimeMemoryLayoutRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Target);
        if (request.ApplicationStaticDataEnd < 0 || request.WasmPageSize <= 0)
        {
            throw new InvalidOperationException("The NetWasm application memory layout is invalid.");
        }

        var initialHeapSize = request.InitialHeapSizeBytes ?? request.Target.DefaultInitialHeapSizeBytes;
        var maximumMemorySize = request.MaximumMemorySizeBytes ?? request.Target.DefaultMaximumMemorySizeBytes;
        if (initialHeapSize < 0 ||
            maximumMemorySize <= 0 ||
            maximumMemorySize > request.Target.MaximumMemorySizeBytes ||
            maximumMemorySize % request.WasmPageSize != 0)
        {
            throw new InvalidOperationException("The requested NetWasm memory limits are invalid.");
        }

        try
        {
            var runtimeGlobalBase = Align(request.ApplicationStaticDataEnd, request.Target.Alignment);
            var heapBase = checked(runtimeGlobalBase + request.Target.RuntimeFootprintBytes);
            var initialMemorySize = Align(checked(heapBase + initialHeapSize), request.WasmPageSize);
            if (initialMemorySize > maximumMemorySize)
            {
                throw new InvalidOperationException("The requested NetWasm maximum memory is smaller than the initial layout.");
            }

            return new RuntimeMemoryLayout(
                runtimeGlobalBase,
                heapBase,
                initialMemorySize,
                maximumMemorySize);
        }
        catch (OverflowException exception)
        {
            throw new InvalidOperationException("The requested NetWasm memory layout is too large.", exception);
        }
    }

    private static long Align(long value, long alignment) =>
        checked((value + alignment - 1) / alignment * alignment);
}
