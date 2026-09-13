using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission;

internal sealed record EvaluationStackLocalLayout(
    int I4Base,
    int I8Base,
    int F4Base,
    int F8Base,
    int ReferenceBase,
    int AddressBase,
    int SlotCount)
{
    public int End => checked(AddressBase + SlotCount);

    public int GetLocal(int slot, CliValueKind type)
    {
        if ((uint)slot >= (uint)SlotCount)
        {
            throw new ArgumentOutOfRangeException(nameof(slot));
        }

        return type switch
        {
            CliValueKind.I4 or CliValueKind.ValueType => checked(I4Base + slot),
            CliValueKind.I8 => checked(I8Base + slot),
            CliValueKind.F4 => checked(F4Base + slot),
            CliValueKind.F8 => checked(F8Base + slot),
            CliValueKind.ManagedReference => checked(ReferenceBase + slot),
            CliValueKind.ManagedAddress => checked(AddressBase + slot),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };
    }
}

internal static class WasmLocalLayoutPlanner
{
    public static EvaluationStackLocalLayout CreateEvaluationStack(
        int stackBase,
        int slotCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(stackBase);
        ArgumentOutOfRangeException.ThrowIfNegative(slotCount);

        return new EvaluationStackLocalLayout(
            stackBase,
            checked(stackBase + slotCount),
            checked(stackBase + slotCount * 2),
            checked(stackBase + slotCount * 3),
            checked(stackBase + slotCount * 4),
            checked(stackBase + slotCount * 5),
            slotCount);
    }

    public static int GetEvaluationStackLocal(
        EvaluationStackLocalLayout layout,
        int slot,
        CliValueKind type,
        WasmTargetLayout target)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(target);

        var storageType = type == CliValueKind.ValueType
            ? CliValueKind.ManagedAddress
            : type;
        if (storageType == CliValueKind.NativeInt)
        {
            storageType = target.UsesMemory64 ? CliValueKind.I8 : CliValueKind.I4;
        }

        return !target.UsesMemory64 && storageType is not (
            CliValueKind.I8 or CliValueKind.F4 or CliValueKind.F8)
            ? layout.GetLocal(slot, CliValueKind.I4)
            : layout.GetLocal(slot, storageType);
    }

    public static ImmutableArray<CliValueKind> CreateMethodLocals(
        StructuredMethodHeader header,
        int exceptionGroupCount,
        IReadOnlySet<int> spilledScalarLocals)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentOutOfRangeException.ThrowIfNegative(exceptionGroupCount);
        ArgumentNullException.ThrowIfNull(spilledScalarLocals);

        var types = CreateEvaluationLocals(header, spilledScalarLocals);
        types.Add(CliValueKind.ManagedReference);
        types.Add(CliValueKind.ManagedAddress);
        types.Add(CliValueKind.ManagedReference);
        for (var index = 0; index < checked(exceptionGroupCount * 2); index++)
        {
            types.Add(CliValueKind.I4);
        }
        types.Add(CliValueKind.ManagedAddress);
        types.Add(CliValueKind.ManagedAddress);
        types.Add(CliValueKind.ManagedAddress);
        types.Add(CliValueKind.I4);
        types.Add(CliValueKind.I4);
        types.Add(CliValueKind.I4);
        types.Add(CliValueKind.I8);
        types.Add(CliValueKind.I4);
        types.Add(CliValueKind.I4);
        types.Add(CliValueKind.I4);
        types.Add(CliValueKind.I4);
        types.Add(CliValueKind.I4);
        return types.ToImmutable();
    }

    public static ImmutableArray<CliValueKind> CreateFilterLocals(
        StructuredMethodHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);

        var types = CreateEvaluationLocals(header, ImmutableHashSet<int>.Empty);
        types.Add(CliValueKind.ManagedReference);
        types.Add(CliValueKind.ManagedAddress);
        types.Add(CliValueKind.ManagedReference);
        types.Add(CliValueKind.ManagedAddress);
        types.Add(CliValueKind.ManagedAddress);
        types.Add(CliValueKind.I4);
        types.Add(CliValueKind.I4);
        types.Add(CliValueKind.I4);
        types.Add(CliValueKind.I8);
        return types.ToImmutable();
    }

    private static ImmutableArray<CliValueKind>.Builder CreateEvaluationLocals(
        StructuredMethodHeader header,
        IReadOnlySet<int> spilledScalarLocals)
    {
        var types = ImmutableArray.CreateBuilder<CliValueKind>(
            checked(header.Locals.Length + header.MaxStack * 6 + 16));
        foreach ((var local, var index) in header.LocalSignatureTypes
                     .Select((local, index) => (local, index)))
        {
            types.Add(local.StackKind == CliValueKind.ValueType ||
                spilledScalarLocals.Contains(index)
                ? CliValueKind.ManagedAddress
                : local.StackKind);
        }

        foreach (var type in new[]
                 {
                     CliValueKind.I4,
                     CliValueKind.I8,
                     CliValueKind.F4,
                     CliValueKind.F8,
                     CliValueKind.ManagedReference,
                     CliValueKind.ManagedAddress,
                 })
        {
            for (var slot = 0; slot < header.MaxStack; slot++)
            {
                types.Add(type);
            }
        }

        return types;
    }
}
