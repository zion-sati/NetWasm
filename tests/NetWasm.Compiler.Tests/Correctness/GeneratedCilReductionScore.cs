namespace NetWasm.Compiler.Tests.Correctness;

internal readonly record struct GeneratedCilReductionScore(
    int Inputs,
    int Methods,
    int ExceptionRegions,
    int Blocks,
    int Instructions,
    int Locals,
    int MetadataOperands,
    ulong ConstantComplexity) : IComparable<GeneratedCilReductionScore>
{
    public static GeneratedCilReductionScore Measure(
        GeneratedCilReductionCase candidate)
    {
        var programs = candidate.SupportingMethods.Prepend(candidate.Program).ToArray();
        var instructions = programs
            .SelectMany(program => program.Blocks)
            .SelectMany(block => block.Instructions)
            .ToArray();
        return new(
            candidate.Inputs.Length,
            programs.Length,
            programs.Sum(program => program.ExceptionRegions.Length),
            programs.Sum(program => program.Blocks.Length),
            instructions.Length,
            programs.Sum(program => program.Locals.Length),
            instructions.Count(instruction =>
                instruction.Operand is GeneratedCilOperand.MetadataToken),
            instructions.Aggregate(0UL, (score, instruction) =>
                checked(score + MeasureConstantComplexity(instruction.Operand))));
    }

    public int CompareTo(GeneratedCilReductionScore other)
    {
        var comparisons = new[]
        {
            Inputs.CompareTo(other.Inputs),
            Methods.CompareTo(other.Methods),
            ExceptionRegions.CompareTo(other.ExceptionRegions),
            Blocks.CompareTo(other.Blocks),
            Instructions.CompareTo(other.Instructions),
            Locals.CompareTo(other.Locals),
            MetadataOperands.CompareTo(other.MetadataOperands),
            ConstantComplexity.CompareTo(other.ConstantComplexity),
        };
        return comparisons.FirstOrDefault(comparison => comparison != 0);
    }

    private static ulong MeasureConstantComplexity(GeneratedCilOperand operand) =>
        operand switch
        {
            GeneratedCilOperand.Int32 value => Magnitude(value.Value),
            GeneratedCilOperand.Int64 value => Magnitude(value.Value),
            GeneratedCilOperand.Float32 value =>
                unchecked((uint)BitConverter.SingleToInt32Bits(value.Value)),
            GeneratedCilOperand.Float64 value =>
                unchecked((ulong)BitConverter.DoubleToInt64Bits(value.Value)),
            _ => 0,
        };

    private static ulong Magnitude(long value) => value == long.MinValue
        ? 1UL << 63
        : unchecked((ulong)Math.Abs(value));
}
