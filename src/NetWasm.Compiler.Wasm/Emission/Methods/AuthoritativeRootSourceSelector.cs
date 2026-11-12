using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Arrays;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed class AuthoritativeRootSourceSelector : IAuthoritativeRootSourceSelector
{
    public ImmutableHashSet<RootSource> Select(MethodRootMap roots, ValueFrameLayout values,
        FilterEnvironmentLayout filters, ArrayMetadataCachingPlan arrays)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(filters);
        ArgumentNullException.ThrowIfNull(arrays);
        // A byref argument is authoritative too: it keeps one value for the call unless stored, so
        // its slot is written at entry and on a store, not republished at every safepoint, which
        // a value type's instance methods and async MoveNext otherwise do for their receiver.
        return roots.Slots.Keys.Where(source => !source.IsIndirect &&
            source.Kind is RootSourceKind.Argument or RootSourceKind.Local or RootSourceKind.ManagedAddressArgument &&
            !filters.Captures.ContainsKey(new(source.Kind != RootSourceKind.Local, source.Index)) &&
            !arrays.Slots.Contains(new(source.Kind != RootSourceKind.Local, source.Index)) &&
            (source.Kind == RootSourceKind.Local
                ? !values.SpilledScalarLocals.Contains(source.Index)
                : !values.ArgumentOffsets.ContainsKey(source.Index))).ToImmutableHashSet();
    }
}
