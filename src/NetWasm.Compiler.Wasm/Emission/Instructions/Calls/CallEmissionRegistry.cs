using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Calls;

internal sealed class CallEmissionRegistry : ICallEmissionRegistry
{
    private readonly ImmutableDictionary<CallEmissionKind, ICallEmitter> _emitters;

    public CallEmissionRegistry(IEnumerable<CallEmissionRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        var builder = ImmutableDictionary.CreateBuilder<CallEmissionKind, ICallEmitter>();
        foreach (var registration in registrations)
        {
            ArgumentNullException.ThrowIfNull(registration);
            ArgumentNullException.ThrowIfNull(registration.Emitter);
            if (!builder.TryAdd(registration.Kind, registration.Emitter))
            {
                throw new InvalidOperationException(
                    $"Call emitter '{registration.Kind}' is registered more than once.");
            }
        }

        var missing = Enum.GetValues<CallEmissionKind>()
            .Where(kind => !builder.ContainsKey(kind))
            .ToArray();
        if (missing.Length != 0)
        {
            throw new InvalidOperationException(
                "Call emitters are not registered for: " + string.Join(", ", missing));
        }

        _emitters = builder.ToImmutable();
    }

    public ICallEmitter Get(CallEmissionKind kind) =>
        _emitters.TryGetValue(kind, out var emitter)
            ? emitter
            : throw new InvalidOperationException(
                $"No call emitter is registered for '{kind}'.");
}
