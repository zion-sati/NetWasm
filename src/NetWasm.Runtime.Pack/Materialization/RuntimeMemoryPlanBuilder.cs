using System;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class RuntimeMemoryPlanBuilder : IRuntimeMemoryPlanBuilder
{
    public RuntimeMemoryPlan Build(RuntimeMemoryLayoutRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Target);
        var target = request.Target;
        var pointerSize = target.Target switch
        {
            "wasm32" => 4,
            "wasm64" => 8,
            _ => throw new InvalidOperationException("The NetWasm runtime target is unsupported."),
        };
        if (request.ApplicationStaticDataEnd < 0 || request.WasmPageSize != 65_536 ||
            target.PointerSizeBytes != pointerSize || target.Alignment <= 0 ||
            (target.Alignment & (target.Alignment - 1)) != 0 || target.NativeStackSizeBytes < 0 ||
            target.NativeStackSizeBytes % target.Alignment != 0)
            throw new InvalidOperationException("The NetWasm application memory layout is invalid.");

        var initialHeap = request.InitialHeapSizeBytes ?? target.DefaultInitialHeapSizeBytes;
        var maximum = request.MaximumMemorySizeBytes ?? target.DefaultMaximumMemorySizeBytes;
        if (initialHeap < 0 || maximum <= 0 || maximum > target.MaximumMemorySizeBytes ||
            maximum % request.WasmPageSize != 0 || target.NativeStackSizeBytes > maximum ||
            (pointerSize == 4 && maximum > 4_294_967_296))
            throw new InvalidOperationException("The requested NetWasm memory limits are invalid.");

        try
        {
            var globalBase = checked((request.ApplicationStaticDataEnd + target.Alignment - 1) /
                target.Alignment * target.Alignment);
            if (globalBase > maximum)
                throw new InvalidOperationException("The requested NetWasm maximum memory is smaller than the application layout.");
            return new(target.Target, pointerSize, request.WasmPageSize, target.Alignment,
                request.ApplicationStaticDataEnd, globalBase, initialHeap, maximum, target.NativeStackSizeBytes);
        }
        catch (OverflowException exception)
        {
            throw new InvalidOperationException("The requested NetWasm memory layout is too large.", exception);
        }
    }
}
