using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class RuntimeIntrinsicEmitterRegistry : IRuntimeIntrinsicEmitterRegistry
{
    private readonly ImmutableDictionary<RuntimeIntrinsic, IRuntimeIntrinsicEmitter> _emitters;

    public RuntimeIntrinsicEmitterRegistry(
        IEnumerable<RuntimeIntrinsicEmitterRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        var materialized = registrations.ToArray();
        foreach (var registration in materialized)
        {
            ArgumentNullException.ThrowIfNull(registration);
            ArgumentNullException.ThrowIfNull(registration.Emitter);
        }
        var duplicate = materialized
            .GroupBy(registration => registration.Intrinsic)
            .FirstOrDefault(group => group.Count() != 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Runtime intrinsic '{duplicate.Key}' has " +
                $"{duplicate.Count()} emitters.");
        }

        _emitters = materialized.ToImmutableDictionary(
            registration => registration.Intrinsic,
            registration => registration.Emitter);
        var missing = Enum.GetValues<RuntimeIntrinsic>()
            .Where(intrinsic => !_emitters.ContainsKey(intrinsic))
            .ToArray();
        if (missing.Length != 0)
        {
            throw new InvalidOperationException(
                "Runtime intrinsics have no emitter: " + string.Join(", ", missing));
        }
    }

    public IRuntimeIntrinsicEmitter Get(RuntimeIntrinsic intrinsic) => _emitters[intrinsic];
}
