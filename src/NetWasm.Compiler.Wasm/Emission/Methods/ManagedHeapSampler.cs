using System;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed class ManagedHeapSampler : IManagedHeapSampler
{
    public long Sample() => GC.GetTotalMemory(forceFullCollection: false);
}
