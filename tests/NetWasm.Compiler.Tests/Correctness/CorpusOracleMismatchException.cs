namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class CorpusOracleMismatchException(int input, string message)
    : InvalidOperationException(message)
{
    public int Input { get; } = input;
}
