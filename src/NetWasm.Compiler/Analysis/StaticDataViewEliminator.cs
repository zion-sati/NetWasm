using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal sealed class StaticDataViewEliminator(
    ICalledMethodResolver calledMethods,
    IStaticDataViewResolver views,
    StaticDataViewEliminationPolicy policy) : IManagedMethodOptimizer
{
    private readonly StaticDataViewEliminationPolicy _policy = policy switch
    {
        StaticDataViewEliminationPolicy.Disabled or
        StaticDataViewEliminationPolicy.Conservative => policy,
        _ => throw new ArgumentOutOfRangeException(nameof(policy)),
    };

    public CilMethodBody Optimize(ManagedMethodBody method)
    {
        ArgumentNullException.ThrowIfNull(method);
        if (_policy == StaticDataViewEliminationPolicy.Disabled)
        {
            return method.Body;
        }

        var candidates = method.ControlFlow.Graph.Blocks
            .Where(block => method.ControlFlow.Graph.ReachableBlocks.Contains(block.Index))
            .SelectMany(block => FindCandidates(method, block))
            .ToArray();
        if (candidates.Length == 0)
        {
            return method.Body;
        }

        var localUses = method.Body.Instructions
            .Where(IsLocalAccess)
            .GroupBy(instruction => ((CilOperand.Index)instruction.Operand).Value)
            .ToDictionary(
                group => group.Key,
                group => group.Select(instruction => instruction.Offset).ToImmutableHashSet());
        var selected = candidates
            .GroupBy(candidate => candidate.Local)
            .Where(group => localUses[group.Key].SetEquals(
                group.SelectMany(candidate => candidate.LocalAccessOffsets)))
            .SelectMany(group => group)
            .ToArray();
        if (selected.Length == 0)
        {
            return method.Body;
        }

        var instructions = method.Body.Instructions.ToArray();
        var indices = instructions
            .Select((instruction, index) => (instruction.Offset, index))
            .ToDictionary(item => item.Offset, item => item.index);
        foreach (var candidate in selected)
        {
            ReplaceWithNop(candidate.ProducerOffset);
            ReplaceWithNop(candidate.StoreOffset);
            ReplaceWithNop(candidate.AddressOffset);
            if (candidate.LoadOffset is null)
            {
                // The view's length is the data's element count.
                var lengthIndex = indices[candidate.IndexerOffset];
                instructions[lengthIndex] = instructions[lengthIndex] with
                {
                    Operation = CilOperation.LoadInt32,
                    Operand = new CilOperand.ConstantI4(candidate.View.ElementCount),
                };
                continue;
            }
            ReplaceWithNop(candidate.IndexerOffset);
            var loadIndex = indices[candidate.LoadOffset.Value];
            instructions[loadIndex] = instructions[loadIndex] with
            {
                Operation = CilOperation.LoadStaticDataElement,
                Operand = candidate.View,
            };
        }

        return method.Body with { Instructions = [.. instructions] };

        void ReplaceWithNop(int offset)
        {
            var index = indices[offset];
            instructions[index] = instructions[index] with
            {
                Operation = CilOperation.Nop,
                Operand = new CilOperand.None(),
            };
        }
    }

    private IEnumerable<Candidate> FindCandidates(
        ManagedMethodBody method,
        BasicBlock block)
    {
        var instructions = block.Instructions;
        for (var producerIndex = 0; producerIndex < instructions.Length; producerIndex++)
        {
            var producer = instructions[producerIndex];
            if (producer.Operation != CilOperation.Call ||
                !method.ControlFlow.InstructionEntryStacks.TryGetValue(
                    producer.Offset,
                    out var producerStack))
            {
                continue;
            }

            var target = calledMethods.Resolve(producer);
            var view = target is null ? null : views.Resolve(target);
            if (view is null)
            {
                continue;
            }
            if (!TryGetNextNonNop(instructions, producerIndex, out var storeIndex) ||
                instructions[storeIndex] is not
                {
                    Operation: CilOperation.StoreLocal,
                    Operand: CilOperand.Index storeLocal,
                } store ||
                !method.Body.LocalSignatureTypes[storeLocal.Value].Equals(view.SpanType))
            {
                continue;
            }
            if (!TryGetNextNonNop(instructions, storeIndex, out var addressIndex) ||
                instructions[addressIndex] is not
                {
                    Operation: CilOperation.LoadLocalAddress,
                    Operand: CilOperand.Index addressLocal,
                } address ||
                addressLocal.Value != storeLocal.Value)
            {
                continue;
            }

            // A read of the view's length.
            if (TryGetNextNonNop(instructions, addressIndex, out var lengthIndex) &&
                IsLength(instructions[lengthIndex], view))
            {
                yield return new(
                    storeLocal.Value,
                    view,
                    producer.Offset,
                    store.Offset,
                    address.Offset,
                    instructions[lengthIndex].Offset,
                    null,
                    [store.Offset, address.Offset]);
                continue;
            }
            var indexerIndex = FindIndexer(
                method,
                instructions,
                addressIndex,
                producerStack.Length,
                view);
            if (indexerIndex < 0)
            {
                continue;
            }

            var indexer = instructions[indexerIndex];
            if (!TryGetNextNonNop(instructions, indexerIndex, out var loadIndex))
            {
                continue;
            }
            var load = instructions[loadIndex];
            if (load.Operation != CilOperation.LoadObject ||
                load.Operand is not CilOperand.TypeIdentity loadedType ||
                !string.Equals(LoadsAs(loadedType.Value), LoadsAs(view.ElementType), StringComparison.Ordinal))
            {
                continue;
            }

            yield return new(
                storeLocal.Value,
                view,
                producer.Offset,
                store.Offset,
                address.Offset,
                indexer.Offset,
                load.Offset,
                [store.Offset, address.Offset]);
        }
    }

    private int FindIndexer(
        ManagedMethodBody method,
        ImmutableArray<CilInstruction> instructions,
        int addressIndex,
        int receiverSlot,
        CilOperand.ReadOnlySpanData view)
    {
        for (var index = addressIndex + 1; index < instructions.Length; index++)
        {
            var instruction = instructions[index];
            if (instruction.Operation == CilOperation.Nop)
            {
                continue;
            }
            if (instruction.Operation == CilOperation.Call)
            {
                var target = calledMethods.Resolve(instruction);
                return IsIndexer(target, view) ? index : -1;
            }
            var consumedOperands = IndexExpressionOperandCount(instruction.Operation);
            var stack = method.ControlFlow.InstructionEntryStacks[instruction.Offset];
            if (consumedOperands < 0 ||
                stack.Length < receiverSlot + consumedOperands + 1 ||
                stack[receiverSlot] != CliValueKind.ManagedAddress)
            {
                return -1;
            }
        }

        return -1;
    }

    // The caller's own read is still a call; an inlined callee's already reads
    // the length field, as accessors are canonicalized before inlining.
    private bool IsLength(CilInstruction instruction, CilOperand.ReadOnlySpanData view) => instruction switch
    {
        { Operation: CilOperation.Call } => calledMethods.Resolve(instruction) is
        {
            Definition: { IsStatic: false, Name: "get_Length" },
            Signature.ParameterSignatureTypes.IsEmpty: true,
        } target && target.DeclaringType.Equals(view.SpanType),
        { Operation: CilOperation.LoadField, Operand: CilOperand.FieldInstance { Value: var field } } =>
            field.Definition.Name == "_length" && field.DeclaringType.Equals(view.SpanType),
        _ => false,
    };

    private static bool IsIndexer(
        MethodInstanceModel? target,
        CilOperand.ReadOnlySpanData view)
    {
        if (target is null ||
            target.Definition.IsStatic ||
            target.Definition.Name != "get_Item" ||
            !target.DeclaringType.Equals(view.SpanType) ||
            target.Signature.ParameterSignatureTypes is not [var indexType] ||
            indexType.CanonicalName != "primitive:i4")
        {
            return false;
        }

        var returnType = target.Signature.ReturnSignatureType;
        while (returnType.Shape == CliTypeShape.Modified)
        {
            returnType = returnType.ElementType!;
        }
        return returnType.Shape == CliTypeShape.ManagedByReference &&
            returnType.ElementType!.Equals(view.ElementType);
    }

    private static int IndexExpressionOperandCount(CilOperation operation) =>
        operation switch
        {
            CilOperation.LoadArgument or
            CilOperation.LoadLocal or
            CilOperation.LoadInt32 => 0,
            CilOperation.Negate or
            CilOperation.OnesComplement or
            CilOperation.ConvertInt32 or
            CilOperation.ConvertInt32Unsigned => 1,
            CilOperation.Add or
            CilOperation.Subtract or
            CilOperation.Multiply or
            CilOperation.BitwiseAnd or
            CilOperation.BitwiseOr or
            CilOperation.BitwiseXor or
            CilOperation.ShiftLeft or
            CilOperation.ShiftRightSigned or
            CilOperation.ShiftRightUnsigned => 2,
            _ => -1,
        };

    // Roslyn reads a bool with ldind.u1 and a char with ldind.u2, which load
    // the same value from the same bytes.
    private static string LoadsAs(CliTypeIdentity type) => type.CanonicalName switch
    {
        "primitive:bool" => "primitive:u1",
        "primitive:char" => "primitive:u2",
        var name => name,
    };

    private static bool TryGetNextNonNop(
        ImmutableArray<CilInstruction> instructions,
        int current,
        out int index)
    {
        for (var next = current + 1; next < instructions.Length; next++)
        {
            if (instructions[next].Operation != CilOperation.Nop)
            {
                index = next;
                return true;
            }
        }

        index = -1;
        return false;
    }

    private static bool IsLocalAccess(CilInstruction instruction) =>
        (instruction.Operation is
            CilOperation.LoadLocal or
            CilOperation.LoadLocalAddress or
            CilOperation.StoreLocal) &&
        instruction.Operand is CilOperand.Index;

    private sealed record Candidate(
        int Local,
        CilOperand.ReadOnlySpanData View,
        int ProducerOffset,
        int StoreOffset,
        int AddressOffset,
        int IndexerOffset,
        int? LoadOffset,
        ImmutableArray<int> LocalAccessOffsets);
}
