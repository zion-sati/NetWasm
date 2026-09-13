using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal sealed class SharedReturnEpilogueRewriter : IMethodRewriteRule
{
    public CilMethodBody Rewrite(CilMethodBody body)
    {
        var instructions = body.Instructions.ToArray();
        var indicesByOffset = instructions
            .Select((instruction, index) => (instruction.Offset, index))
            .ToDictionary(item => item.Offset, item => item.index);
        var changed = false;
        for (var index = 0; index < instructions.Length; index++)
        {
            var branch = instructions[index];
            if (branch.Operation != CilOperation.Branch ||
                branch.Operand is not CilOperand.BranchTarget target ||
                !indicesByOffset.TryGetValue(target.Offset, out var targetIndex))
            {
                continue;
            }
            var epilogueIndex = SkipNops(instructions, targetIndex);
            if (epilogueIndex >= instructions.Length)
            {
                continue;
            }
            if (instructions[epilogueIndex].Operation == CilOperation.Return)
            {
                instructions[index] = branch with
                {
                    Operation = CilOperation.Return,
                    Operand = new CilOperand.None(),
                };
                changed = true;
                continue;
            }
            if (instructions[epilogueIndex].Operation != CilOperation.LoadLocal ||
                instructions[epilogueIndex].Operand is not CilOperand.Index loadedLocal)
            {
                continue;
            }
            var returnIndex = SkipNops(instructions, epilogueIndex + 1);
            var storeIndex = PreviousNonNop(instructions, index - 1);
            if (returnIndex >= instructions.Length ||
                instructions[returnIndex].Operation != CilOperation.Return ||
                storeIndex < 0 ||
                instructions[storeIndex].Operation != CilOperation.StoreLocal ||
                instructions[storeIndex].Operand is not CilOperand.Index storedLocal ||
                storedLocal.Value != loadedLocal.Value)
            {
                continue;
            }
            instructions[storeIndex] = instructions[storeIndex] with
            {
                Operation = CilOperation.Nop,
                Operand = new CilOperand.None(),
            };
            instructions[index] = branch with
            {
                Operation = CilOperation.Return,
                Operand = new CilOperand.None(),
            };
            changed = true;
        }
        return changed ? body with { Instructions = [.. instructions] } : body;
    }

    private static int SkipNops(CilInstruction[] source, int index)
    {
        while (index < source.Length && source[index].Operation == CilOperation.Nop)
        {
            index++;
        }
        return index;
    }

    private static int PreviousNonNop(CilInstruction[] source, int index)
    {
        while (index >= 0 && source[index].Operation == CilOperation.Nop)
        {
            index--;
        }
        return index;
    }
}
