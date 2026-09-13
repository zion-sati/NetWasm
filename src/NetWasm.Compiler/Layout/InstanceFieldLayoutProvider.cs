using System;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal sealed class InstanceFieldLayoutProvider(
    ManagedLayoutSnapshot snapshot) : IInstanceFieldLayoutProvider
{
    private readonly ManagedLayoutSnapshot _snapshot = snapshot ??
        throw new ArgumentNullException(nameof(snapshot));

    public FieldLayout GetFieldLayout(EntityKey field) =>
        _snapshot.TypeLayouts.ValueLayoutState.Fields.TryGetValue(field, out var layout)
            ? layout
            : throw Missing(field);

    public FieldLayout GetFieldLayout(FieldInstanceModel field) =>
        field.IsConstructed
            ? _snapshot.TypeLayouts.ValueLayoutState.ConstructedFields.TryGetValue(
                field.CanonicalName,
                out var layout)
                ? layout
                : throw Missing(field.CanonicalName)
            : GetFieldLayout(field.Definition.Key);

    private static CompilerException Missing(object field) => new(new CompilerDiagnostic(
        DiagnosticCode.RuntimeContract,
        $"field layout for '{field}' was not generated"));
}
