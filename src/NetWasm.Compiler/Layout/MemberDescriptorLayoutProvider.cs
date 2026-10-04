using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal sealed class MemberDescriptorLayoutProvider(
    ManagedLayoutSnapshot snapshot) : IMemberDescriptorLayout
{
    private readonly ManagedLayoutSnapshot _snapshot = snapshot ??
        throw new ArgumentNullException(nameof(snapshot));

    public int DeclaringTypeIdOffset =>
        _snapshot.StaticData.MemberDescriptorDeclaringTypeIdOffset;
    public int RequiresDeclaringTypeOffset =>
        _snapshot.StaticData.MemberDescriptorRequiresDeclaringTypeOffset;

    public int GetDescriptorAddress(MethodInstanceModel method) =>
        _snapshot.StaticData.MethodDescriptors.TryGetValue(
            method.CanonicalName,
            out var address)
            ? address
            : throw Missing("method", method.CanonicalName);

    public int GetDescriptorAddress(FieldInstanceModel field) =>
        _snapshot.StaticData.FieldDescriptors.TryGetValue(field.CanonicalName, out var address)
            ? address
            : throw Missing("field", field.CanonicalName);

    private static CompilerException Missing(string kind, string identity) => new(
        new CompilerDiagnostic(
            DiagnosticCode.RuntimeContract,
            $"{kind} descriptor '{identity}' was not assigned static storage"));
}
