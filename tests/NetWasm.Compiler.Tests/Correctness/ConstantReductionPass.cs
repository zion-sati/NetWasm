using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class ConstantReductionPass : IGeneratedCilReductionPass
{
    public string Name => "constants";

    public IEnumerable<GeneratedCilReductionCase> Generate(
        GeneratedCilReductionCase candidate)
    {
        for (var blockIndex = 0;
             blockIndex < candidate.Program.Blocks.Length;
             blockIndex++)
        {
            var block = candidate.Program.Blocks[blockIndex];
            for (var instructionIndex = 0;
                 instructionIndex < block.Instructions.Length;
                 instructionIndex++)
            {
                foreach (var operand in ReducedOperands(
                             block.Instructions[instructionIndex].Operand))
                {
                    var instruction = block.Instructions[instructionIndex] with
                    {
                        Operand = operand,
                    };
                    yield return candidate with
                    {
                        Program = candidate.Program with
                        {
                            Blocks = candidate.Program.Blocks.SetItem(
                                blockIndex,
                                block with
                                {
                                    Instructions = block.Instructions.SetItem(
                                        instructionIndex,
                                        instruction),
                                }),
                        },
                    };
                }
            }
        }
    }

    private static ImmutableArray<GeneratedCilOperand> ReducedOperands(
        GeneratedCilOperand operand) => operand switch
        {
            GeneratedCilOperand.Int32 value => CanonicalInt32(value.Value),
            GeneratedCilOperand.Int64 value => CanonicalInt64(value.Value),
            GeneratedCilOperand.Float32 value when value.Value != 0 =>
                [new GeneratedCilOperand.Float32(0)],
            GeneratedCilOperand.Float64 value when value.Value != 0 =>
                [new GeneratedCilOperand.Float64(0)],
            _ => [],
        };

    private static ImmutableArray<GeneratedCilOperand> CanonicalInt32(int value) =>
        [.. new[] { 0, 1, -1 }
            .Where(candidate => candidate != value)
            .Where(candidate => Magnitude(candidate) < Magnitude(value))
            .Select(candidate =>
                (GeneratedCilOperand)new GeneratedCilOperand.Int32(candidate))];

    private static ImmutableArray<GeneratedCilOperand> CanonicalInt64(long value) =>
        [.. new long[] { 0, 1, -1 }
            .Where(candidate => candidate != value)
            .Where(candidate => Magnitude(candidate) < Magnitude(value))
            .Select(candidate =>
                (GeneratedCilOperand)new GeneratedCilOperand.Int64(candidate))];

    private static ulong Magnitude(long value) => value == long.MinValue
        ? 1UL << 63
        : unchecked((ulong)Math.Abs(value));
}
