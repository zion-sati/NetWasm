using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.Attributes;

// Strategy: a finite exact-value analysis. Merging different proofs loses the
// proof; it never turns a runtime choice into a set of retained types.
internal sealed class AttributeProvenanceAnalyzer(
    ICalledMethodResolver calls,
    ITypeOperandResolver types,
    IReadonlyTypeFieldValueResolver fields,
    ImmutableHashSet<EntityKey> nullableUnwrapMethods,
    CliTypeIdentity nullableDefinition) : IAttributeProvenanceAnalyzer
{
    public ImmutableDictionary<int, ImmutableArray<AttributeValueProof>> Analyze(ValidatedControlFlowGraph controlFlow)
    {
        ArgumentNullException.ThrowIfNull(controlFlow);
        var graph = controlFlow.Graph;
        var body = graph.MethodBody;
        var addressedLocals = body.Instructions
            .Where(instruction => instruction.Operation == CilOperation.LoadLocalAddress)
            .Select(instruction => ((CilOperand.Index)instruction.Operand).Value)
            .ToImmutableHashSet();
        var unwindWrites = body.ExceptionRegions
            .Where(region => region.Kind is CilExceptionRegionKind.Finally or CilExceptionRegionKind.Fault or CilExceptionRegionKind.Filter)
            .SelectMany(region => WrittenLocals(
                region.Kind == CilExceptionRegionKind.Filter ? region.FilterOffset!.Value : region.HandlerOffset,
                region.Kind == CilExceptionRegionKind.Filter ? region.HandlerOffset : region.HandlerOffset + region.HandlerLength))
            .ToImmutableHashSet();
        var entries = new Dictionary<int, ProofState>();
        var pending = new Queue<int>();
        var results = ImmutableDictionary.CreateBuilder<int, ImmutableArray<AttributeValueProof>>();
        Merge(graph.Entry.Index, new([], new AttributeValueProof[body.Locals.Length]));

        while (pending.TryDequeue(out var blockIndex))
        {
            var block = graph.GetBlock(blockIndex);
            var entry = entries[blockIndex];
            var stack = entry.Stack.ToList();
            var locals = (AttributeValueProof[])entry.Locals.Clone();
            bool? condition = null;
            bool? filterAccepted = null;
            for (var index = 0; index < block.Instructions.Length; index++)
            {
                var instruction = block.Instructions[index];
                results[instruction.Offset] = [.. stack];
                // An exception can leave before the current instruction's writes.
                // Use the handler's validated stack and preserve local proofs only
                // when every incoming exceptional edge agrees.
                if (CilSafepointClassifier.MayTransferControlExceptionally(instruction))
                {
                    foreach (var successor in graph.ExceptionalSuccessors[blockIndex])
                    {
                        var exceptionalLocals = (AttributeValueProof[])locals.Clone();
                        foreach (var local in unwindWrites)
                        {
                            exceptionalLocals[local] = default;
                        }
                        Merge(successor, new(
                            new AttributeValueProof[controlFlow.EntryStacks[successor].Length], exceptionalLocals));
                    }
                }
                if (instruction.Operation == CilOperation.EndFilter)
                {
                    filterAccepted = stack[^1].Condition;
                }
                if (instruction.Operation is CilOperation.BranchIfTrue or CilOperation.BranchIfFalse)
                {
                    condition = stack[^1].Condition;
                    if (instruction.Operation == CilOperation.BranchIfFalse && condition is { } value)
                    {
                        condition = !value;
                    }
                }
                var outgoingCount = index + 1 < block.Instructions.Length
                    ? controlFlow.InstructionEntryStacks[block.Instructions[index + 1].Offset].Length
                    : graph.Successors[blockIndex].IsEmpty
                        ? 0
                        : controlFlow.EntryStacks[graph.Successors[blockIndex][0]].Length;
                Transfer(instruction, outgoingCount, stack, locals);
            }

            if (block.Terminator.Operation == CilOperation.EndFilter && filterAccepted != false)
            {
                foreach (var region in body.ExceptionRegions.Where(region =>
                    region.Kind == CilExceptionRegionKind.Filter &&
                    block.Terminator.Offset >= region.FilterOffset && block.Terminator.Offset < region.HandlerOffset))
                {
                    var handler = graph.GetBlockAtOffset(region.HandlerOffset).Index;
                    Merge(handler, new(new AttributeValueProof[controlFlow.EntryStacks[handler].Length], locals));
                }
            }
            foreach (var successor in graph.Successors[blockIndex])
            {
                if (condition is { } taken && block.Terminator.Operand is CilOperand.BranchTarget target &&
                    taken != (graph.GetBlock(successor).StartOffset == target.Offset) &&
                    target.Offset != block.Terminator.NextOffset)
                {
                    continue;
                }
                Merge(successor, new([.. stack], locals));
            }
        }
        return results.ToImmutable();

        IEnumerable<int> WrittenLocals(int start, int end) => body.Instructions
            .Where(instruction => instruction.Offset >= start && instruction.Offset < end &&
                instruction.Operation == CilOperation.StoreLocal)
            .Select(instruction => ((CilOperand.Index)instruction.Operand).Value);

        void Merge(int block, ProofState incoming)
        {
            if (!entries.TryGetValue(block, out var existing))
            {
                entries.Add(block, new((AttributeValueProof[])incoming.Stack.Clone(),
                    (AttributeValueProof[])incoming.Locals.Clone()));
                pending.Enqueue(block);
                return;
            }
            var changed = Join(existing.Stack, incoming.Stack) | Join(existing.Locals, incoming.Locals);
            if (changed)
            {
                pending.Enqueue(block);
            }
        }

        void Transfer(CilInstruction instruction, int outgoingCount,
            List<AttributeValueProof> stack, AttributeValueProof[] locals)
        {
            var operation = instruction.Operation;
            switch (operation)
            {
                case CilOperation.LoadTypeToken:
                    stack.Add(new(AttributeValueKind.TypeToken, types.Resolve(instruction, body.MethodInstance)));
                    return;
                case CilOperation.MaterializeType:
                    stack[^1] = stack[^1].Kind == AttributeValueKind.TypeToken
                        ? stack[^1] with { Kind = AttributeValueKind.Type }
                        : default;
                    return;
                case CilOperation.LoadNull:
                    stack.Add(new(AttributeValueKind.Null));
                    return;
                case CilOperation.LoadInt32:
                    stack.Add(new(AttributeValueKind.Int32, Integer: ((CilOperand.ConstantI4)instruction.Operand).Value));
                    return;
                case CilOperation.Duplicate:
                    stack.Add(stack[^1]);
                    return;
                case CilOperation.LoadLocal:
                    var loaded = ((CilOperand.Index)instruction.Operand).Value;
                    stack.Add(addressedLocals.Contains(loaded) ? default : locals[loaded]);
                    return;
                case CilOperation.StoreLocal:
                    locals[((CilOperand.Index)instruction.Operand).Value] = stack[^1];
                    stack.RemoveAt(stack.Count - 1);
                    return;
                case CilOperation.CastClass:
                    // A successful cast preserves identity; a failed cast does not
                    // reach the query. Keep the original cast in the executable CIL.
                    return;
                case CilOperation.Leave:
                    var destination = ((CilOperand.BranchTarget)instruction.Operand).Offset;
                    var crossedFinally = body.ExceptionRegions.Where(region =>
                        region.Kind == CilExceptionRegionKind.Finally &&
                        instruction.Offset >= region.TryOffset && instruction.Offset < region.TryOffset + region.TryLength &&
                        (destination < region.TryOffset || destination >= region.TryOffset + region.TryLength)).ToArray();
                    foreach (var region in crossedFinally)
                    {
                        foreach (var local in WrittenLocals(region.HandlerOffset, region.HandlerOffset + region.HandlerLength))
                        {
                            locals[local] = default;
                        }
                    }
                    foreach (var region in crossedFinally)
                    {
                        var handler = graph.GetBlockAtOffset(region.HandlerOffset).Index;
                        Merge(handler, new(new AttributeValueProof[controlFlow.EntryStacks[handler].Length], locals));
                    }
                    return;
                case CilOperation.LoadField when instruction.Operand is CilOperand.FieldInstance field:
                    stack[^1] = fields.Resolve(field.Value) is { } type
                        ? new(AttributeValueKind.Type, type)
                        : default;
                    return;
                case CilOperation.LoadField when instruction.Operand is CilOperand.Entity field:
                    stack[^1] = fields.Resolve(field.Key) is { } fieldType
                        ? new(AttributeValueKind.Type, fieldType)
                        : default;
                    return;
                case CilOperation.Call:
                case CilOperation.CallVirtual:
                case CilOperation.NewObject:
                    var method = calls.Resolve(instruction)!;
                    var consumed = method.Signature.ParameterTypes.Length +
                        (operation != CilOperation.NewObject && !method.Definition.IsStatic ? 1 : 0);
                    var result = operation == CilOperation.Call &&
                        nullableUnwrapMethods.Contains(method.Definition.Key) &&
                        stack[^1] is { Kind: AttributeValueKind.Type, Type: { } argument }
                            ? Unwrap(argument)
                            : default;
                    stack.RemoveRange(stack.Count - consumed, consumed);
                    if (operation == CilOperation.NewObject || method.Signature.ReturnType != CliValueKind.Void)
                    {
                        stack.Add(result);
                    }
                    return;
                case CilOperation.CallIndirect:
                    var signature = ((CilOperand.CallSite)instruction.Operand).Signature;
                    var indirectConsumed = signature.ParameterTypes.Length + 1;
                    stack.RemoveRange(stack.Count - indirectConsumed, indirectConsumed);
                    if (signature.ReturnType != CliValueKind.Void)
                    {
                        stack.Add(default);
                    }
                    return;
            }

            // Other CIL instructions produce at most one value (dup and calls
            // are handled above). Validated stack heights identify the consumed
            // suffix without losing unaffected values beneath it.
            var produced = operation switch
            {
                CilOperation.Nop or CilOperation.Pop or CilOperation.Branch or
                CilOperation.BranchIfTrue or CilOperation.BranchIfFalse or
                CilOperation.BranchIfEqual or CilOperation.BranchIfNotEqual or
                CilOperation.BranchIfGreaterThanSigned or CilOperation.BranchIfGreaterThanUnsigned or
                CilOperation.BranchIfGreaterThanOrEqualSigned or CilOperation.BranchIfGreaterThanOrEqualUnsigned or
                CilOperation.BranchIfLessThanSigned or CilOperation.BranchIfLessThanUnsigned or
                CilOperation.BranchIfLessThanOrEqualSigned or CilOperation.BranchIfLessThanOrEqualUnsigned or
                CilOperation.Switch or CilOperation.StoreArgument or CilOperation.StoreField or
                CilOperation.StoreStaticField or CilOperation.StoreObject or CilOperation.CopyObject or
                CilOperation.InitializeObject or CilOperation.CopyBlock or CilOperation.InitializeBlock or
                CilOperation.InitializeArrayData or CilOperation.StoreArrayElementReference or
                CilOperation.StoreArrayElement or CilOperation.StoreRectangularArrayElement or
                CilOperation.Constrained or CilOperation.Volatile or CilOperation.Readonly or
                CilOperation.Unaligned or CilOperation.Break or CilOperation.Throw or CilOperation.Rethrow or
                CilOperation.Leave or CilOperation.EndFinally or CilOperation.EndFilter or CilOperation.Return => 0,
                _ => 1,
            };
            var preserved = Math.Max(0, outgoingCount - produced);
            stack.RemoveRange(preserved, stack.Count - preserved);
            while (stack.Count < outgoingCount)
            {
                stack.Add(default);
            }
        }

        AttributeValueProof Unwrap(CliTypeIdentity type) =>
            type.Shape == CliTypeShape.GenericInstantiation && type.ElementType!.Equals(nullableDefinition)
                ? new(AttributeValueKind.Type, type.TypeArguments[0])
                : new(AttributeValueKind.Null);
    }

    private static bool Join(AttributeValueProof[] existing, AttributeValueProof[] incoming)
    {
        var changed = false;
        for (var index = 0; index < existing.Length; index++)
        {
            if (existing[index].Kind != AttributeValueKind.Unknown && existing[index] != incoming[index])
            {
                existing[index] = default;
                changed = true;
            }
        }
        return changed;
    }

    private sealed record ProofState(AttributeValueProof[] Stack, AttributeValueProof[] Locals);
}
