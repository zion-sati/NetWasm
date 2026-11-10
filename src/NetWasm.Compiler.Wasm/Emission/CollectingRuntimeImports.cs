using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Linq;

namespace NetWasm.Compiler.Wasm.Emission;

// The runtime functions that can collect, because they allocate managed memory
// or run managed code or finalizers: a caller's live references must be
// published before calling one. Throwing allocates too, but leaves the frame,
// whose handlers' references the exceptional root analysis keeps.
internal sealed class CollectingRuntimeImports(IRuntimeImportResolver runtimeImports) : ICollectingRuntimeImportResolver
{
    private readonly ConcurrentDictionary<RuntimeImportSelection, FrozenDictionary<uint, RuntimeImportSymbol>> _indices = new();

    public static readonly FrozenSet<RuntimeImportSymbol> Symbols = new[]
    {
        RuntimeImportSymbol.Allocate,
        RuntimeImportSymbol.AllocateFinalizable,
        RuntimeImportSymbol.AllocateObject,
        RuntimeImportSymbol.AllocateObjectFallback,
        RuntimeImportSymbol.AllocateReferenceArray,
        RuntimeImportSymbol.AllocateValueArray,
        RuntimeImportSymbol.AllocateRectangularArray,
        RuntimeImportSymbol.AllocateBoundedRectangularArray,
        RuntimeImportSymbol.AllocateString,
        RuntimeImportSymbol.ArrayClone,
        RuntimeImportSymbol.ArrayCopy,
        RuntimeImportSymbol.ArrayGetValue,
        RuntimeImportSymbol.BoxedValueClone,
        RuntimeImportSymbol.GetTypeObject,
        RuntimeImportSymbol.Collect,
        RuntimeImportSymbol.FinalizerSafepoint,
        RuntimeImportSymbol.GcWaitForPendingFinalizers,
        RuntimeImportSymbol.ComponentReallocate,
        RuntimeImportSymbol.NativeAlloc,
        RuntimeImportSymbol.NativeRealloc,
        RuntimeImportSymbol.NativeAlignedAlloc,
        RuntimeImportSymbol.NativeAlignedRealloc,
    }.ToFrozenSet();

    // Those a module with the selected imports has, by the index a call to one
    // names there: leaving imports out renumbers the ones after them.
    public FrozenDictionary<uint, RuntimeImportSymbol> Resolve(RuntimeImportSelection selection) =>
        _indices.GetOrAdd(selection, selection => Symbols
            .Where(selection.Includes)
            .ToFrozenDictionary(symbol => (uint)runtimeImports.Resolve(symbol, selection)));
}
