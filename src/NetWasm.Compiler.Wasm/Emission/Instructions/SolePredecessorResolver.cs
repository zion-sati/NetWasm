using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions;

internal interface ISolePredecessorResolver
{
    CilInstruction? Resolve(InstructionEmissionRequest request);
}

// Strategy: the instruction that alone leads to this one, falling through to it, when no branch
// or switch also enters here. A lexical predecessor is not proof when another path skips it.
internal sealed class SolePredecessorResolver : ISolePredecessorResolver
{
    public CilInstruction? Resolve(InstructionEmissionRequest request)
    {
        var body = request.Header.Instructions;
        var offset = request.Instruction.Offset;
        var index = body.IndexOf(request.Instruction);
        if (index <= 0 || body[index - 1].NextOffset != offset)
            return null;
        foreach (var candidate in body)
        {
            if (candidate.Operand is CilOperand.BranchTarget branch && branch.Offset == offset ||
                candidate.Operand is CilOperand.SwitchTargets targets && targets.Offsets.Contains(offset))
                return null;
        }
        return body[index - 1];
    }
}
