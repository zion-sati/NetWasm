using System;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal sealed class StaticFieldLayoutProvider(
    ManagedLayoutSnapshot snapshot) : IStaticFieldLayoutProvider
{
    private readonly ManagedLayoutSnapshot _snapshot = snapshot ??
        throw new ArgumentNullException(nameof(snapshot));

    public StaticFieldLayout GetStaticFieldLayout(EntityKey field) =>
        _snapshot.StaticData.StaticFields.TryGetValue(field, out var layout)
            ? layout
            : throw Missing(field);

    public StaticFieldLayout GetStaticFieldLayout(FieldInstanceModel field) =>
        field.IsConstructed
            ? _snapshot.StaticData.ConstructedStaticFields.TryGetValue(
                field.CanonicalName,
                out var layout)
                ? layout
                : throw Missing(field.CanonicalName)
            : GetStaticFieldLayout(field.Definition.Key);

    private static CompilerException Missing(object field) => new(new CompilerDiagnostic(
        DiagnosticCode.RuntimeContract,
        $"static field layout for '{field}' was not generated"));
}
