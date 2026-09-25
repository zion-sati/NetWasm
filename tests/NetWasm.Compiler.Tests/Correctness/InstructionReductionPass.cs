namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class InstructionReductionPass : IGeneratedCilReductionPass
{
    public string Name => "instructions-and-metadata";

    public IEnumerable<GeneratedCilReductionCase> Generate(
        GeneratedCilReductionCase candidate)
    {
        for (var blockIndex = 0;
             blockIndex < candidate.Program.Blocks.Length;
             blockIndex++)
        {
            var block = candidate.Program.Blocks[blockIndex];
            foreach (var length in RangeLengths(block.Instructions.Length))
            {
                for (var start = 0;
                     start <= block.Instructions.Length - length;
                     start++)
                {
                    yield return candidate with
                    {
                        Program = candidate.Program with
                        {
                            Blocks = candidate.Program.Blocks.SetItem(
                                blockIndex,
                                block with
                                {
                                    Instructions = block.Instructions.RemoveRange(
                                        start,
                                        length),
                                }),
                        },
                    };
                }
            }
        }
    }

    private static IEnumerable<int> RangeLengths(int count)
    {
        for (var length = Math.Max(1, count / 2); length >= 1; length /= 2)
        {
            yield return length;
            if (length == 1)
            {
                yield break;
            }
        }
    }
}
