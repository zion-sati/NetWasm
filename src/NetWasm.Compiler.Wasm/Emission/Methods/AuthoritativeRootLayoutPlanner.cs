using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed class AuthoritativeRootLayoutPlanner : IAuthoritativeRootLayoutPlanner
{
    public AuthoritativeRootLayout Plan(MethodRootMap roots, ImmutableHashSet<RootSource> sources)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(sources);
        foreach (var source in sources)
        {
            if (source.IsIndirect ||
                source.Kind is not (RootSourceKind.Argument or RootSourceKind.Local or RootSourceKind.ManagedAddressArgument) ||
                !roots.Slots.ContainsKey(source))
                throw new ArgumentException("Authoritative storage requires a mapped direct reference variable.", nameof(sources));
        }
        if (sources.IsEmpty)
            return new(roots, ImmutableDictionary<RootSource, int>.Empty);

        var stable = ImmutableDictionary.CreateBuilder<RootSource, int>();
        foreach (var source in sources.OrderBy(source => source.Kind).ThenBy(source => source.Index))
            stable.Add(source, stable.Count);

        var slots = stable.ToImmutable().ToBuilder();
        var residualSlots = new Dictionary<int, int>();
        foreach (var (source, oldSlot) in roots.Slots.OrderBy(pair => pair.Value)
                     .ThenBy(pair => pair.Key.Kind).ThenBy(pair => pair.Key.Index)
                     .ThenBy(pair => pair.Key.ByteOffset))
        {
            if (sources.Contains(source)) continue;
            if (!residualSlots.TryGetValue(oldSlot, out var slot))
            {
                slot = checked(stable.Count + residualSlots.Count);
                residualSlots.Add(oldSlot, slot);
            }
            slots.Add(source, slot);
        }
        return new(roots with { Slots = slots.ToImmutable() }, stable.ToImmutable());
    }
}
