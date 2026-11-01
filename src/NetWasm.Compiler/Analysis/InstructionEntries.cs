using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal static class InstructionEntries
{
    // Offsets that control can reach other than from the instruction before:
    // branch targets and protected region boundaries.
    public static HashSet<int> Of(CilMethodBody body)
    {
        var entries = new HashSet<int>();
        foreach (var instruction in body.Instructions)
        {
            if (instruction.Operand is CilOperand.BranchTarget target)
            {
                entries.Add(target.Offset);
            }
        }
        foreach (var region in body.ExceptionRegions)
        {
            entries.UnionWith([region.TryOffset, region.TryOffset + region.TryLength,
                region.HandlerOffset, region.HandlerOffset + region.HandlerLength]);
            if (region.FilterOffset is { } filter) entries.Add(filter);
        }
        return entries;
    }
}
