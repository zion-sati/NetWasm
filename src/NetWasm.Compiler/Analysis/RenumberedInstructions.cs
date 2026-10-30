using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// A rewrite that inserts instructions renumbers the body. Branches, switches and
// protected regions follow each original offset to where its replacement starts.
internal static class RenumberedInstructions
{
    public static CilOperand Operand(CilOperand operand, IReadOnlyDictionary<int, int> starts) => operand switch
    {
        CilOperand.BranchTarget target => new CilOperand.BranchTarget(starts[target.Offset]),
        CilOperand.SwitchTargets targets => new CilOperand.SwitchTargets(
            [.. targets.Offsets.Select(offset => starts[offset])]),
        _ => operand,
    };

    public static CilExceptionRegion Region(CilExceptionRegion region, IReadOnlyDictionary<int, int> starts)
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
            FilterOffset = region.FilterOffset is { } filter
                ? starts[filter]
                : null,
        };
    }

    // A branch from one replacement list to the instruction at index in the same
    // list, which has no original offset.
    public static CilOperand.BranchTarget Local(int index) => new(-(index + 1));

    // The body with each instruction replaced by its list of replacements, in
    // order, numbered from 0.
    public static CilMethodBody Splice(CilMethodBody body, List<CilInstruction>[] replacements)
    {
        var starts = new Dictionary<int, int>();
        var count = 0;
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            starts.Add(body.Instructions[index].Offset, count);
            count += replacements[index].Count;
        }
        starts.Add(body.Instructions[^1].NextOffset, count);
        var rewritten = ImmutableArray.CreateBuilder<CilInstruction>(count);
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            var source = body.Instructions[index].SourceOffset;
            var listStart = rewritten.Count;
            foreach (var instruction in replacements[index])
            {
                rewritten.Add(new CilInstruction(rewritten.Count, rewritten.Count + 1, instruction.Operation,
                    instruction.Operand is CilOperand.BranchTarget { Offset: < 0 and var local }
                        ? new CilOperand.BranchTarget(listStart - local - 1)
                        : Operand(instruction.Operand, starts))
                {
                    OriginalOffset = source,
                });
            }
        }
        return body with
        {
            Instructions = rewritten.MoveToImmutable(),
            ExceptionRegions = [.. body.ExceptionRegions.Select(region => Region(region, starts))],
        };
    }
}
