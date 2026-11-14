using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Values.AggregateAnalysis;

internal sealed class ManagedAggregateBlockTransfer(
    IMethodRepository methodRepository,
    IManagedAggregateRootObligationRecorder rootObligations,
    IManagedAggregateInstructionTransfer instructions) : IManagedAggregateBlockTransfer
{
    private readonly IMethodRepository _methodRepository = methodRepository ??
        throw new ArgumentNullException(nameof(methodRepository));
    private readonly IManagedAggregateRootObligationRecorder _rootObligations = rootObligations ??
        throw new ArgumentNullException(nameof(rootObligations));
    private readonly IManagedAggregateInstructionTransfer _instructions = instructions ??
        throw new ArgumentNullException(nameof(instructions));

    public ManagedAggregateFlowState Apply(
        StructuredBlockId blockId,
        ManagedAggregateFlowState entry,
        ManagedAggregateAnalysisSession session)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(session);
        var method = session.Request.Method;
        var block = method.Blocks[blockId];
        var stack = entry.Stack.ToList();
        var locals = entry.Locals.ToBuilder();
        for (var index = 0; index < block.Instructions.Length; index++)
        {
            var instruction = block.Instructions[index];
            _rootObligations.Record(instruction.Offset, stack, locals, session);
            var produced = ProducedCount(instruction);
            var outgoingKinds = ManagedAggregateBlockExits.OutgoingKinds(method, block, index);
            var consumed = stack.Count + produced - outgoingKinds.Length;
            if (consumed < 0 || consumed > stack.Count)
            {
                throw new InvalidOperationException(
                    $"Aggregate value analysis disagrees with the validated CIL stack " +
                    $"at IL_{instruction.Offset:x4} ({instruction.Operation}): " +
                    $"entry={stack.Count}, produced={produced}, " +
                    $"outgoing={outgoingKinds.Length}.");
            }
            _instructions.Apply(new(blockId, instruction, produced, consumed, outgoingKinds), stack, locals, session);
        }

        ApplyExit(method, block, stack, locals, session);
        return new([.. stack], locals.ToImmutable());
    }

    private void ApplyExit(
        StructuredMethod method,
        StructuredBlockDefinition block,
        List<ManagedAggregateAddressOrigins> stack,
        ImmutableArray<ManagedAggregateAddressOrigins>.Builder locals,
        ManagedAggregateAnalysisSession session)
    {
        if (ManagedAggregateBlockExits.InstructionOffset(block.Exit) is { } offset)
        {
            _rootObligations.Record(offset, stack, locals, session);
        }

        var successors = ManagedAggregateBlockExits.Successors(block.Exit);
        var outgoingCount = successors.IsEmpty
            ? 0
            : method.Blocks[successors[0]].EntryStack.Length;
        var consumed = stack.Count - outgoingCount;
        if (consumed < 0)
        {
            throw new InvalidOperationException(
                "Aggregate value analysis encountered an invalid structured exit stack.");
        }
        session.RejectConsumed(stack, consumed);
        stack.RemoveRange(outgoingCount, consumed);
    }

    private int ProducedCount(CilInstruction instruction) => instruction.Operation switch
    {
        CilOperation.Call or CilOperation.CallVirtual =>
            CallSignature(instruction).ReturnType == CliValueKind.Void ? 0 : 1,
        CilOperation.CallIndirect =>
            ((CilOperand.CallSite)instruction.Operand).Signature.ReturnType ==
            CliValueKind.Void ? 0 : 1,
        CilOperation.Nop or CilOperation.Pop or CilOperation.Branch or
            CilOperation.BranchIfTrue or CilOperation.BranchIfFalse or
            CilOperation.BranchIfEqual or CilOperation.BranchIfNotEqual or
            CilOperation.BranchIfGreaterThanSigned or
            CilOperation.BranchIfGreaterThanUnsigned or
            CilOperation.BranchIfGreaterThanOrEqualSigned or
            CilOperation.BranchIfGreaterThanOrEqualUnsigned or
            CilOperation.BranchIfLessThanSigned or
            CilOperation.BranchIfLessThanUnsigned or
            CilOperation.BranchIfLessThanOrEqualSigned or
            CilOperation.BranchIfLessThanOrEqualUnsigned or
            CilOperation.StoreArgument or
            CilOperation.StoreLocal or CilOperation.StoreField or
            CilOperation.StoreStaticField or CilOperation.StoreObject or
            CilOperation.CopyObject or CilOperation.InitializeObject or
            CilOperation.CopyBlock or CilOperation.InitializeBlock or
            CilOperation.InitializeArrayData or
            CilOperation.StoreArrayElementReference or
            CilOperation.StoreArrayElement or
            CilOperation.StoreRectangularArrayElement or
            CilOperation.Constrained or CilOperation.Volatile or
            CilOperation.Readonly or CilOperation.Unaligned or
            CilOperation.Break or CilOperation.Throw or
            CilOperation.Rethrow or CilOperation.Leave or
            CilOperation.EndFinally or CilOperation.EndFilter or
            CilOperation.Return => 0,
        _ => 1,
    };

    private MethodSignatureModel CallSignature(CilInstruction instruction) =>
        instruction.Operand is CilOperand.MethodInstance method
            ? method.Value.Signature
            : _methodRepository.GetMethod(((CilOperand.Entity)instruction.Operand).Key)
                .Signature;
}
