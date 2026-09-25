namespace NetWasm.Compiler.Tests.Correctness;

internal static class RandomCilDiagnosticReplaySelector
{
    public static int SelectCase(int input, int inputCount, int caseCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(input);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(inputCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(caseCount);

        if (input >= inputCount || inputCount % caseCount != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(input));
        }

        return input / (inputCount / caseCount);
    }
}
