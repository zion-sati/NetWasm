namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class InputReductionPass : IGeneratedCilReductionPass
{
    public string Name => "inputs";

    public IEnumerable<GeneratedCilReductionCase> Generate(
        GeneratedCilReductionCase candidate)
    {
        for (var index = 0; index < candidate.Inputs.Length; index++)
        {
            yield return candidate with
            {
                Inputs = candidate.Inputs.RemoveAt(index),
            };
        }
    }
}
