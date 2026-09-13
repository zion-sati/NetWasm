using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal interface ICilSwitchLowerer
{
    LoweredSwitchBody Lower(
        ImmutableArray<CilInstruction> instructions,
        ImmutableArray<CilExceptionRegion> exceptionRegions);
}

internal sealed class CilSwitchLowerer : ICilSwitchLowerer
{
    public LoweredSwitchBody Lower(
        ImmutableArray<CilInstruction> instructions,
        ImmutableArray<CilExceptionRegion> exceptionRegions)
    {
        if (!instructions.Any(instruction => instruction.Operation == CilOperation.Switch))
        {
            return new LoweredSwitchBody(
                instructions,
                exceptionRegions,
                Changed: false,
                AdditionalMaxStack: 0);
        }

        var starts = new Dictionary<int, int>();
        var nextOffset = 0;
        foreach (var instruction in instructions)
        {
            starts.Add(instruction.Offset, nextOffset);
            nextOffset += ExpandedLength(instruction);
        }
        var originalEnd = instructions[^1].NextOffset;
        starts.Add(originalEnd, nextOffset);

        var result = ImmutableArray.CreateBuilder<CilInstruction>(nextOffset);
        foreach (var instruction in instructions)
        {
            if (instruction.Operand is not CilOperand.SwitchTargets targets)
            {
                result.Add(new CilInstruction(
                    result.Count,
                    result.Count + 1,
                    instruction.Operation,
                    RewriteOperand(instruction.Operand, starts)));
                continue;
            }

            for (var index = 0; index < targets.Offsets.Length; index++)
            {
                Add(CilOperation.Duplicate, new CilOperand.None());
                Add(CilOperation.LoadInt32, new CilOperand.ConstantI4(index));
                Add(
                    CilOperation.BranchIfNotEqual,
                    new CilOperand.BranchTarget(result.Count + 3));
                Add(CilOperation.Pop, new CilOperand.None());
                Add(
                    CilOperation.Branch,
                    new CilOperand.BranchTarget(starts[targets.Offsets[index]]));
            }
            Add(CilOperation.Pop, new CilOperand.None());
        }

        return new LoweredSwitchBody(
            result.ToImmutable(),
            [.. exceptionRegions.Select(region => RewriteRegion(region, starts))],
            Changed: true,
            AdditionalMaxStack: 2);

        void Add(CilOperation operation, CilOperand operand) => result.Add(
            new CilInstruction(result.Count, result.Count + 1, operation, operand));
    }

    private static int ExpandedLength(CilInstruction instruction) =>
        instruction.Operand is CilOperand.SwitchTargets targets
            ? checked(targets.Offsets.Length * 5 + 1)
            : 1;

    private static CilOperand RewriteOperand(
        CilOperand operand,
        Dictionary<int, int> starts) => operand is CilOperand.BranchTarget target
            ? new CilOperand.BranchTarget(starts[target.Offset])
            : operand;

    private static CilExceptionRegion RewriteRegion(
        CilExceptionRegion region,
        Dictionary<int, int> starts)
    {
        var tryStart = starts[region.TryOffset];
        var tryEnd = starts[region.TryOffset + region.TryLength];
        var handlerStart = starts[region.HandlerOffset];
        var handlerEnd = starts[region.HandlerOffset + region.HandlerLength];
        return region with
        {
            TryOffset = tryStart,
            TryLength = tryEnd - tryStart,
            HandlerOffset = handlerStart,
            HandlerLength = handlerEnd - handlerStart,
            FilterOffset = region.FilterOffset is int filter
                ? starts[filter]
                : null,
        };
    }
}

internal sealed record LoweredSwitchBody(
    ImmutableArray<CilInstruction> Instructions,
    ImmutableArray<CilExceptionRegion> ExceptionRegions,
    bool Changed,
    int AdditionalMaxStack);
