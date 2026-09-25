using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class LocalReductionPass : IGeneratedCilReductionPass
{
    private static readonly HashSet<CilOperation> LocalOperations =
    [
        CilOperation.LoadLocal,
        CilOperation.LoadLocalAddress,
        CilOperation.StoreLocal,
    ];

    public string Name => "locals";

    public IEnumerable<GeneratedCilReductionCase> Generate(
        GeneratedCilReductionCase candidate)
    {
        if (candidate.Program.Locals.IsEmpty)
        {
            yield break;
        }
        var last = candidate.Program.Locals.Length - 1;
        var used = candidate.Program.Blocks
            .SelectMany(block => block.Instructions)
            .Any(instruction =>
                LocalOperations.Contains(instruction.Operation) &&
                instruction.Operand is GeneratedCilOperand.Index index &&
                index.Value == last);
        if (!used)
        {
            yield return candidate with
            {
                Program = candidate.Program with
                {
                    Locals = candidate.Program.Locals.RemoveAt(last),
                },
            };
        }
    }
}
