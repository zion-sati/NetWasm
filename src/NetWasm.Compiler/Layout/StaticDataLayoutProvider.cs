using System;
using System.Collections.Immutable;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal sealed class StaticDataLayoutProvider(
    ManagedLayoutSnapshot snapshot) : IManagedStaticDataLayout
{
    private readonly ManagedLayoutSnapshot _snapshot = snapshot ??
        throw new ArgumentNullException(nameof(snapshot));

    public int StaticDataEnd => _snapshot.StaticDataEnd;
    public ImmutableArray<int> StaticRootAddresses => _snapshot.StaticRootAddresses;
    public ImmutableArray<DataSegment> DataSegments => _snapshot.DataSegments;
    public int DeclaringTypeIdOffset =>
        _snapshot.StaticData.MemberDescriptorDeclaringTypeIdOffset;
    public int RequiresDeclaringTypeOffset =>
        _snapshot.StaticData.MemberDescriptorRequiresDeclaringTypeOffset;
    public int TypeFactsTableAddress => _snapshot.StaticData.TypeFactsTableAddress;
    public int TypeFactsTableCount => _snapshot.StaticData.TypeFactsTableCount;

    public StringLayout GetStringLayout(string value) =>
        _snapshot.StaticData.Strings.TryGetValue(value, out var layout)
            ? layout
            : throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.RuntimeContract,
                "string literal was not assigned static storage"));

    public int GetMethodDescriptorAddress(MethodInstanceModel method) =>
        _snapshot.StaticData.MethodDescriptors.TryGetValue(
            method.CanonicalName,
            out var address)
            ? address
            : throw Missing("method", method.CanonicalName);

    public int GetFieldDescriptorAddress(FieldInstanceModel field) =>
        _snapshot.StaticData.FieldDescriptors.TryGetValue(field.CanonicalName, out var address)
            ? address
            : throw Missing("field", field.CanonicalName);

    private static CompilerException Missing(string kind, string identity) => new(
        new CompilerDiagnostic(
            DiagnosticCode.RuntimeContract,
            $"{kind} descriptor '{identity}' was not assigned static storage"));
}
