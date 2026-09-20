using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions;

internal interface IInstructionFamilyResolver
{
    InstructionFamily Resolve(CilOperation operation);
}

internal sealed record InstructionFamilyRegistration(
    InstructionFamily Family,
    ImmutableArray<CilOperation> Operations);

internal sealed class InstructionFamilyCatalog : IInstructionFamilyResolver
{
    private readonly ImmutableDictionary<CilOperation, InstructionFamily> _families;

    public InstructionFamilyCatalog(
        IEnumerable<InstructionFamilyRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        var families =
            ImmutableDictionary.CreateBuilder<CilOperation, InstructionFamily>();
        foreach (var registration in registrations)
        {
            ArgumentNullException.ThrowIfNull(registration);
            foreach (var operation in registration.Operations)
            {
                if (!families.TryAdd(operation, registration.Family))
                {
                    throw new InvalidOperationException(
                        $"CIL operation '{operation}' belongs to more than one family.");
                }
            }
        }

        var missing = SupportedCil.Operations
            .Where(operation => !families.ContainsKey(operation))
            .ToArray();
        if (missing.Length != 0)
        {
            throw new InvalidOperationException(
                $"CIL operation '{missing[0]}' has no instruction family.");
        }

        _families = families.ToImmutable();
    }

    public InstructionFamily Resolve(CilOperation operation) =>
        _families.TryGetValue(operation, out var family)
            ? family
            : throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
}
