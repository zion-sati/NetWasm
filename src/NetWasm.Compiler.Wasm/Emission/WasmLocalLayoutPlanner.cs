using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission;

// One local per evaluation-stack slot for each Wasm value type. Pointer-sized
// kinds use the i32 bank on Wasm32 and the i64 bank on Wasm64.
internal sealed record EvaluationStackLocalLayout(
    int I4Base,
    int I8Base,
    int F4Base,
    int F8Base,
    int V128Base,
    int SlotCount)
{
    public int End => checked(V128Base + SlotCount);

    public int GetLocal(int slot, CliValueKind type)
    {
        if ((uint)slot >= (uint)SlotCount)
        {
            throw new ArgumentOutOfRangeException(nameof(slot));
        }

        return type switch
        {
            CliValueKind.I4 => checked(I4Base + slot),
            CliValueKind.I8 => checked(I8Base + slot),
            CliValueKind.F4 => checked(F4Base + slot),
            CliValueKind.F8 => checked(F8Base + slot),
            CliValueKind.V128 => checked(V128Base + slot),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };
    }
}

internal static class WasmLocalLayoutPlanner
{
    // The scratch locals every emitted body declares after its frame locals, in
    // the order MethodEmissionContext numbers them from InteropDescriptor: the
    // interop descriptor, result and handle, the I4 and I8 numeric temporaries,
    // the dispatcher program counter, the second I4 temporary and the inline
    // catch frame. Methods, filter funclets and generated bodies all declare
    // this list, so no derived temporary is left undeclared or mistyped.
    public static ImmutableArray<CliValueKind> TemporaryLocals { get; } =
    [
        CliValueKind.ManagedAddress,
        CliValueKind.I4,
        CliValueKind.I4,
        CliValueKind.I4,
        CliValueKind.I8,
        CliValueKind.I4,
        CliValueKind.I4,
        CliValueKind.I4,
    ];

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
            slotCount);
    }

    // The locals CreateEvaluationStack numbers, in its order: one bank of
    // slotCount locals per Wasm value type.
    public static IEnumerable<CliValueKind> EvaluationStackLocalTypes(int slotCount) =>
        new[] { CliValueKind.I4, CliValueKind.I8, CliValueKind.F4, CliValueKind.F8, CliValueKind.V128 }
            .SelectMany(type => Enumerable.Repeat(type, slotCount));

    public static int GetEvaluationStackLocal(
        EvaluationStackLocalLayout layout,
        int slot,
        CliValueKind type,
        WasmTargetLayout target)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(target);

        // A slot keeps one local per Wasm value type. Native ints, object references, managed
        // addresses and value-type addresses are pointer-sized, so they share the slot's i32 local on
        // Wasm32 and its i64 local, with longs, on Wasm64. IL may read a slot as another kind of the
        // same width, an unmanaged pointer passed where a byref is declared for instance, and the
        // local it reads is then the one the value was stored in.
        var storageType = type switch
        {
            CliValueKind.F4 or CliValueKind.F8 or CliValueKind.I4 or CliValueKind.I8 or CliValueKind.V128 => type,
            _ => target.UsesMemory64 ? CliValueKind.I8 : CliValueKind.I4,
        };
        return layout.GetLocal(slot, storageType);
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
        types.AddRange(TemporaryLocals);
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
        types.AddRange(TemporaryLocals);
        return types.ToImmutable();
    }

    private static ImmutableArray<CliValueKind>.Builder CreateEvaluationLocals(
        StructuredMethodHeader header,
        IReadOnlySet<int> spilledScalarLocals)
    {
        var types = ImmutableArray.CreateBuilder<CliValueKind>(
            checked(header.Locals.Length + header.MaxStack * 5 + 16));
        foreach ((var local, var index) in header.LocalSignatureTypes
                     .Select((local, index) => (local, index)))
        {
            types.Add(local.StackKind == CliValueKind.ValueType ||
                spilledScalarLocals.Contains(index)
                ? CliValueKind.ManagedAddress
                : local.StackKind);
        }

        types.AddRange(EvaluationStackLocalTypes(header.MaxStack));

        return types;
    }
}
