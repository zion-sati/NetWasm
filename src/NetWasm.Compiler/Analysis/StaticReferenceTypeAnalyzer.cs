// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal interface IStaticReferenceTypeAnalyzer
{
    // Each instruction's entry stack, by offset: every slot's static type, or null where the value's
    // producers disagree or declare none.
    ImmutableDictionary<int, ImmutableArray<CliTypeIdentity?>> Analyze(ValidatedControlFlowGraph controlFlow);
}

// Strategy: a forward analysis of the declared type of every stack value, from the local, argument,
// field, method result, constructor, new array or cast that produced it, closed over the body's generic
// context, an array's element type for its element, a string for a string literal. Where paths meet with different types the slot is unknown, so a sealed
// declared type is the value's exact type when it is not null; a null literal takes the type of the values it meets,
// which describes them when they are not null. Every handler and filter of a reachable protected region
// starts with nothing known, as the graph reaches it: a filter's handler, a finally that runs on leave
// and the code that follows only them are analyzed like any other code.
internal sealed class StaticReferenceTypeAnalyzer(
    ICalledMethodResolver calls,
    IFieldRepository fields) : IStaticReferenceTypeAnalyzer
{
    private static readonly CliTypeIdentity String =
        CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, isValueType: false);

    // A null literal's slot until it meets a typed value.
    private static readonly CliTypeIdentity NullLiteral =
        CliTypeIdentity.Primitive("<null literal>", CliValueKind.ManagedReference, isValueType: false);

    public ImmutableDictionary<int, ImmutableArray<CliTypeIdentity?>> Analyze(ValidatedControlFlowGraph controlFlow)
    {
        ArgumentNullException.ThrowIfNull(controlFlow);
        var graph = controlFlow.Graph;
        var body = graph.MethodBody;
        var entries = new Dictionary<int, CliTypeIdentity?[]>();
        var pending = new Queue<int>();
        var results = ImmutableDictionary.CreateBuilder<int, ImmutableArray<CliTypeIdentity?>>();
        Merge(graph.Entry.Index, []);
        foreach (var region in body.ExceptionRegions)
        {
            Enter(region.HandlerOffset);
            if (region.FilterOffset is { } filter) Enter(filter);
        }
        while (pending.TryDequeue(out var blockIndex))
        {
            var block = graph.GetBlock(blockIndex);
            var stack = entries[blockIndex].ToList();
            for (var index = 0; index < block.Instructions.Length; index++)
            {
                var instruction = block.Instructions[index];
                results[instruction.Offset] = [.. stack.Select(static type => ReferenceEquals(type, NullLiteral) ? null : type)];
                var outgoing = index + 1 < block.Instructions.Length
                    ? controlFlow.InstructionEntryStacks[block.Instructions[index + 1].Offset].Length
                    : graph.Successors[blockIndex].IsEmpty
                        ? 0
                        : controlFlow.EntryStacks[graph.Successors[blockIndex][0]].Length;
                Transfer(body, instruction, outgoing, stack);
            }
            foreach (var successor in graph.Successors[blockIndex])
            {
                Merge(successor, [.. stack]);
            }
        }
        return results.ToImmutable();

        void Enter(int offset)
        {
            var handler = graph.GetBlockAtOffset(offset).Index;
            if (graph.ReachableBlocks.Contains(handler))
            {
                Merge(handler, new CliTypeIdentity?[controlFlow.EntryStacks[handler].Length]);
            }
        }

        void Merge(int block, CliTypeIdentity?[] incoming)
        {
            if (!entries.TryGetValue(block, out var existing))
            {
                entries.Add(block, incoming);
                pending.Enqueue(block);
                return;
            }
            var changed = false;
            for (var slot = 0; slot < existing.Length && slot < incoming.Length; slot++)
            {
                var joined = Join(existing[slot], incoming[slot]);
                if (!Equals(joined, existing[slot]))
                {
                    existing[slot] = joined;
                    changed = true;
                }
            }
            if (changed)
            {
                pending.Enqueue(block);
            }
        }
    }

    private void Transfer(CilMethodBody body, CilInstruction instruction, int outgoing, List<CliTypeIdentity?> stack)
    {
        if (instruction.Operation == CilOperation.Duplicate)
        {
            stack.Add(stack[^1]);
            return;
        }
        // An element read from an array has the array's static element type.
        var element = instruction.Operation == CilOperation.LoadArrayElementReference && stack.Count >= 2 &&
            stack[^2] is { Shape: CliTypeShape.SzArray, ElementType: { } elementType }
                ? elementType
                : null;
        var produced = Produced(instruction);
        var preserved = Math.Max(0, outgoing - produced);
        stack.RemoveRange(preserved, stack.Count - preserved);
        while (stack.Count < outgoing)
        {
            stack.Add(null);
        }
        if (produced == 1 && outgoing > 0 && (element ?? Declared(body, instruction)) is { } type)
        {
            var closed = type.Substitute(
                body.MethodInstance?.DeclaringType.TypeArguments ?? ImmutableArray<CliTypeIdentity>.Empty,
                body.MethodInstance?.MethodArguments ?? ImmutableArray<CliTypeIdentity>.Empty);
            stack[^1] = closed.ContainsGenericParameters ? null : closed;
        }
    }

    private static CliTypeIdentity? Join(CliTypeIdentity? existing, CliTypeIdentity? incoming) =>
        existing is null || incoming is null ? null
        : ReferenceEquals(existing, NullLiteral) ? incoming
        : ReferenceEquals(incoming, NullLiteral) || Equals(existing, incoming) ? existing
        : null;

    // Whether the instruction leaves one value on top: the stack heights decide what it consumed.
    private int Produced(CilInstruction instruction) => StackPushes.Of(instruction, calls);

    private CliTypeIdentity? Declared(CilMethodBody body, CilInstruction instruction) => instruction switch
    {
        { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var local } }
            when local < body.LocalSignatureTypes.Length => body.LocalSignatureTypes[local],
        { Operation: CilOperation.LoadArgument, Operand: CilOperand.Index { Value: var argument } } =>
            Argument(body, argument),
        { Operation: CilOperation.LoadField or CilOperation.LoadStaticField, Operand: CilOperand.FieldInstance field } =>
            field.Value.FieldType,
        { Operation: CilOperation.LoadField or CilOperation.LoadStaticField, Operand: CilOperand.Entity field } =>
            fields.GetField(field.Key).SignatureType,
        { Operation: CilOperation.Call or CilOperation.CallVirtual } =>
            calls.Resolve(instruction)?.Signature.ReturnSignatureType,
        { Operation: CilOperation.NewObject } => calls.Resolve(instruction)?.DeclaringType,
        { Operation: CilOperation.NewArray, Operand: CilOperand.TypeIdentity element } =>
            CliTypeIdentity.SzArray(element.Value),
        { Operation: CilOperation.CastClass or CilOperation.IsInstance, Operand: CilOperand.TypeIdentity cast } =>
            cast.Value,
        { Operation: CilOperation.LoadArrayElement, Operand: CilOperand.TypeIdentity element } => element.Value,
        { Operation: CilOperation.LoadString } => String,
        { Operation: CilOperation.LoadNull } => NullLiteral,
        _ => null,
    };

    private static CliTypeIdentity? Argument(CilMethodBody body, int argument)
    {
        if (body.MethodInstance is not { } method)
        {
            return null;
        }
        var parameter = method.Definition.IsStatic ? argument : argument - 1;
        return parameter < 0
            ? method.DeclaringType
            : parameter < method.Signature.ParameterSignatureTypes.Length
                ? method.Signature.ParameterSignatureTypes[parameter]
                : null;
    }
}
