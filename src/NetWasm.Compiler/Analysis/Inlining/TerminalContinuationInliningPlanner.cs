using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Inlining;

internal sealed class TerminalContinuationInliningPlanner :
    ITerminalContinuationInliningPlanner
{
    public TerminalContinuationInliningPlan? Plan(
        TerminalContinuationInliningRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = request.CallerBody;
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(request.Inlining);
        ArgumentNullException.ThrowIfNull(request.InstructionEntryStacks);

        if ((uint)request.CallIndex >= (uint)body.Instructions.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        var call = body.Instructions[request.CallIndex];
        var method = request.Inlining.Method;
        if (!method.Definition.IsStatic ||
            method.Signature.ReturnType != CliValueKind.Void ||
            call.Operation != CilOperation.Call ||
            request.CallIndex + 1 >= body.Instructions.Length ||
            call.NextOffset != body.Instructions[request.CallIndex + 1].Offset ||
            !request.InstructionEntryStacks.TryGetValue(
                call.Offset,
                out var callEntryStack) ||
            callEntryStack.Length != method.Signature.ParameterTypes.Length ||
            request.Inlining.Body.Instructions.Any(instruction =>
                instruction.Operation == CilOperation.Return &&
                (!request.Inlining.InstructionEntryStacks.TryGetValue(
                    instruction.Offset,
                    out var returnStack) ||
                 !returnStack.IsEmpty)))
        {
            return null;
        }

        var suffix = body.Instructions[(request.CallIndex + 1)..];
        if (suffix.IsEmpty ||
            suffix[^1].Operation != CilOperation.Return ||
            suffix[..^1].Any(static instruction =>
                IsUnsupportedSuffixOperation(instruction.Operation)) ||
            !HasValidCallerReturnStack(
                body,
                suffix[^1],
                request.InstructionEntryStacks))
        {
            return null;
        }

        var suffixOffsets = suffix
            .Select(static instruction => instruction.Offset)
            .ToHashSet();
        if (body.Instructions.Any(instruction =>
                TargetsAny(instruction.Operand, suffixOffsets)))
        {
            return null;
        }

        return new(suffix, body.Instructions.Length - 1);
    }

    private static bool HasValidCallerReturnStack(
        CilMethodBody body,
        CilInstruction instruction,
        IReadOnlyDictionary<int, ImmutableArray<CliValueKind>> entryStacks)
    {
        if (!entryStacks.TryGetValue(instruction.Offset, out var stack))
        {
            return false;
        }

        var returnType = (body.MethodInstance?.Signature ?? body.Method.Signature)
            .ReturnType;
        return returnType == CliValueKind.Void
            ? stack.IsEmpty
            : stack is [var value] && value == returnType;
    }

    private static bool IsUnsupportedSuffixOperation(CilOperation operation) =>
        operation is
            CilOperation.Branch or
            CilOperation.BranchIfTrue or
            CilOperation.BranchIfFalse or
            CilOperation.BranchIfEqual or
            CilOperation.BranchIfNotEqual or
            CilOperation.BranchIfGreaterThanSigned or
            CilOperation.BranchIfGreaterThanUnsigned or
            CilOperation.BranchIfGreaterThanOrEqualSigned or
            CilOperation.BranchIfGreaterThanOrEqualUnsigned or
            CilOperation.BranchIfLessThanSigned or
            CilOperation.BranchIfLessThanUnsigned or
            CilOperation.BranchIfLessThanOrEqualSigned or
            CilOperation.BranchIfLessThanOrEqualUnsigned or
            CilOperation.Throw or
            CilOperation.Rethrow or
            CilOperation.Leave or
            CilOperation.EndFinally or
            CilOperation.EndFilter or
            CilOperation.Return or
            CilOperation.LocalAllocate or
            CilOperation.Constrained or
            CilOperation.Volatile or
            CilOperation.Readonly or
            CilOperation.Unaligned or
            CilOperation.Break;

    private static bool TargetsAny(
        CilOperand operand,
        HashSet<int> targets) => operand switch
        {
            CilOperand.BranchTarget target => targets.Contains(target.Offset),
            _ => false,
        };
}
