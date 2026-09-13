using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

internal interface IJavaScriptImportResultEmitterRegistry
{
    IJavaScriptImportResultEmitter Get(JavaScriptImportResultKind kind);
}

internal sealed class JavaScriptImportResultEmitterRegistry :
    IJavaScriptImportResultEmitterRegistry
{
    private readonly ImmutableDictionary<JavaScriptImportResultKind,
        IJavaScriptImportResultEmitter> _emitters;

    public JavaScriptImportResultEmitterRegistry(
        IEnumerable<JavaScriptImportResultEmitterRegistration> registrations)
    {
        var materialized = registrations.ToArray();
        var duplicate = materialized
            .GroupBy(registration => registration.Kind)
            .FirstOrDefault(group => group.Count() != 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"JavaScript import result kind '{duplicate.Key}' has " +
                $"{duplicate.Count()} emitters");
        }

        _emitters = materialized.ToImmutableDictionary(
            registration => registration.Kind,
            registration => registration.Emitter);
        var missing = Enum.GetValues<JavaScriptImportResultKind>()
            .Where(kind => !_emitters.ContainsKey(kind))
            .ToArray();
        if (missing.Length != 0)
        {
            throw new InvalidOperationException(
                $"JavaScript import result kinds have no emitter: " +
                $"{string.Join(", ", missing)}");
        }
    }

    public IJavaScriptImportResultEmitter Get(JavaScriptImportResultKind kind) =>
        _emitters.TryGetValue(kind, out var emitter)
            ? emitter
            : throw new InvalidOperationException(
                $"JavaScript import result kind '{kind}' has no emitter");
}
