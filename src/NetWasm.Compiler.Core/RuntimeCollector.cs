// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.Core;

// One of the runtime's garbage collectors, by the name NetWasmGarbageCollector gives it, and what it asks of
// the code compiled for it. A program is compiled for one collector and linked with that collector's runtime.
public sealed record RuntimeCollector
{
    private RuntimeCollector(string name, bool hasGenerations)
    {
        Name = name;
        HasGenerations = hasGenerations;
    }

    public string Name { get; }

    // The collector has generations: it traces an object it kept at an earlier collection only where a
    // reference has been stored into it since, so every store of a reference into memory is followed by a
    // write barrier that records where. CoreLib's GC members that report generations read it too.
    public bool HasGenerations { get; }

    public static RuntimeCollector Compact { get; } = new("Compact", hasGenerations: false);
    public static RuntimeCollector NetWasm { get; } = new("NetWasm", hasGenerations: false);
    public static RuntimeCollector NetWasmGen { get; } = new("NetWasmGen", hasGenerations: true);
    public static RuntimeCollector Bump { get; } = new("Bump", hasGenerations: false);

    public static ImmutableArray<RuntimeCollector> All { get; } = [Compact, NetWasm, NetWasmGen, Bump];

    // The collector of exactly that name, or none.
    public static RuntimeCollector? Named(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return All.FirstOrDefault(collector => string.Equals(collector.Name, name, StringComparison.Ordinal));
    }
}
