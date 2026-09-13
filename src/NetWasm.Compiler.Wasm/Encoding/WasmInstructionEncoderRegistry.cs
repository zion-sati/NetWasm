using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace NetWasm.Compiler.Wasm.Encoding;

/// <summary>
/// Immutable O(1) operand-shape-to-encoder Strategy registry.
/// </summary>
public sealed class WasmInstructionEncoderRegistry : IWasmInstructionEncoderRegistry
{
    private readonly ImmutableDictionary<WasmInstructionOperandShape, IWasmInstructionEncoder>
        _encoders;

    public WasmInstructionEncoderRegistry(
        IEnumerable<WasmInstructionEncoderRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        var encoders = ImmutableDictionary.CreateBuilder<
            WasmInstructionOperandShape,
            IWasmInstructionEncoder>();
        foreach (var registration in registrations)
        {
            ArgumentNullException.ThrowIfNull(registration);
            if (!encoders.TryAdd(registration.Shape, registration.Encoder))
            {
                throw new InvalidOperationException(
                    $"An encoder is already registered for operand shape '{registration.Shape}'.");
            }
        }

        _encoders = encoders.ToImmutable();
    }

    public int Count => _encoders.Count;

    public IWasmInstructionEncoder Resolve(WasmInstructionOperandShape shape) =>
        _encoders.TryGetValue(shape, out var encoder)
            ? encoder
            : throw new InvalidOperationException(
                $"No instruction encoder is registered for operand shape '{shape}'.");

}
