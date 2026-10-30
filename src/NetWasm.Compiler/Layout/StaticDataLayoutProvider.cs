using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal sealed class StaticDataLayoutProvider(
    ManagedLayoutSnapshot snapshot) : IStaticDataLayout
{
    private readonly ManagedLayoutSnapshot _snapshot = snapshot ??
        throw new ArgumentNullException(nameof(snapshot));

    public int StaticDataEnd => _snapshot.StaticDataEnd;
    public ImmutableArray<int> StaticRootAddresses => _snapshot.StaticRootAddresses;
    public ImmutableArray<DataSegment> DataSegments => _snapshot.DataSegments;
    public int TypeFactsTableAddress => _snapshot.StaticData.TypeFactsTableAddress;
    public int TypeFactsTableCount => _snapshot.StaticData.TypeFactsTableCount;
    public int TypeObjectTableAddress => _snapshot.StaticData.TypeObjectTableAddress;
    public int TypeObjectCacheAddress => _snapshot.StaticData.TypeObjectCacheAddress;
    public int TypeObjectCount => _snapshot.StaticData.TypeObjectCount;
    public int TypeObjectSize => _snapshot.StaticData.TypeObjectSize;

    public IReadOnlyDictionary<string, int> TypeObjectAddresses => _snapshot.StaticData.TypeObjects;

    public StringLayout GetStringLayout(string value) =>
        _snapshot.StaticData.Strings.TryGetValue(value, out var layout)
            ? layout
            : throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.RuntimeContract,
                "string literal was not assigned static storage"));
}
