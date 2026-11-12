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
// field, method result, constructor or cast that produced it, closed over the body's generic
// context. Where paths meet with different types the slot is unknown, so a sealed declared type is
// the value's exact type when it is not null.
internal sealed class StaticReferenceTypeAnalyzer(
    ICalledMethodResolver calls,
    IFieldRepository fields) : IStaticReferenceTypeAnalyzer
{
    public ImmutableDictionary<int, ImmutableArray<CliTypeIdentity?>> Analyze(ValidatedControlFlowGraph controlFlow)
    {
        ArgumentNullException.ThrowIfNull(controlFlow);
        var graph = controlFlow.Graph;
        var body = graph.MethodBody;
        var entries = new Dictionary<int, CliTypeIdentity?[]>();
        var pending = new Queue<int>();
        var results = ImmutableDictionary.CreateBuilder<int, ImmutableArray<CliTypeIdentity?>>();
        Merge(graph.Entry.Index, []);
        while (pending.TryDequeue(out var blockIndex))
        {
            var block = graph.GetBlock(blockIndex);
            var stack = entries[blockIndex].ToList();
            foreach (var handler in graph.ExceptionalSuccessors[blockIndex])
            {
                Merge(handler, new CliTypeIdentity?[controlFlow.EntryStacks[handler].Length]);
            }
            for (var index = 0; index < block.Instructions.Length; index++)
            {
                var instruction = block.Instructions[index];
                results[instruction.Offset] = [.. stack];
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
                if (existing[slot] is not null && !Equals(existing[slot], incoming[slot]))
                {
                    existing[slot] = null;
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
        var produced = Produced(instruction);
        var preserved = Math.Max(0, outgoing - produced);
        stack.RemoveRange(preserved, stack.Count - preserved);
        while (stack.Count < outgoing)
        {
            stack.Add(null);
        }
        if (produced == 1 && outgoing > 0 && Declared(body, instruction) is { } type)
        {
            var closed = type.Substitute(
                body.MethodInstance?.DeclaringType.TypeArguments ?? ImmutableArray<CliTypeIdentity>.Empty,
                body.MethodInstance?.MethodArguments ?? ImmutableArray<CliTypeIdentity>.Empty);
            stack[^1] = closed.ContainsGenericParameters ? null : closed;
        }
    }

    // Whether the instruction leaves one value on top: the stack heights decide what it consumed.
    private int Produced(CilInstruction instruction) => instruction.Operation switch
    {
        CilOperation.Call or CilOperation.CallVirtual =>
            calls.Resolve(instruction)?.Signature.ReturnType == CliValueKind.Void ? 0 : 1,
        CilOperation.CallIndirect =>
            ((CilOperand.CallSite)instruction.Operand).Signature.ReturnType == CliValueKind.Void ? 0 : 1,
        _ => instruction.Operation is CilOperation.Nop or CilOperation.Pop or CilOperation.StoreLocal or
            CilOperation.StoreArgument or CilOperation.StoreField or CilOperation.StoreStaticField or
            CilOperation.StoreObject or CilOperation.StoreArrayElement or CilOperation.StoreArrayElementReference or
            CilOperation.StoreRectangularArrayElement or CilOperation.CopyObject or CilOperation.InitializeObject or
            CilOperation.CopyBlock or CilOperation.InitializeBlock or CilOperation.InitializeArrayData or
            CilOperation.Branch or CilOperation.BranchIfTrue or CilOperation.BranchIfFalse or
            CilOperation.BranchIfEqual or CilOperation.BranchIfNotEqual or
            CilOperation.BranchIfGreaterThanSigned or CilOperation.BranchIfGreaterThanUnsigned or
            CilOperation.BranchIfGreaterThanOrEqualSigned or CilOperation.BranchIfGreaterThanOrEqualUnsigned or
            CilOperation.BranchIfLessThanSigned or CilOperation.BranchIfLessThanUnsigned or
            CilOperation.BranchIfLessThanOrEqualSigned or CilOperation.BranchIfLessThanOrEqualUnsigned or
            CilOperation.Constrained or CilOperation.Volatile or CilOperation.Readonly or CilOperation.Unaligned or
            CilOperation.Break or CilOperation.Throw or CilOperation.Rethrow or CilOperation.Leave or
            CilOperation.EndFinally or CilOperation.EndFilter or CilOperation.Return
                ? 0
                : 1,
    };

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
        { Operation: CilOperation.CastClass or CilOperation.IsInstance, Operand: CilOperand.TypeIdentity cast } =>
            cast.Value,
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
