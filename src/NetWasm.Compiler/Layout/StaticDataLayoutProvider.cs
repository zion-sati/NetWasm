using System;
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

    public StringLayout GetStringLayout(string value) =>
        _snapshot.StaticData.Strings.TryGetValue(value, out var layout)
            ? layout
            : throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.RuntimeContract,
                "string literal was not assigned static storage"));
}
