namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class MethodReductionPass : IGeneratedCilReductionPass
{
    public string Name => "methods";

    public IEnumerable<GeneratedCilReductionCase> Generate(
        GeneratedCilReductionCase candidate)
    {
        for (var index = 0; index < candidate.SupportingMethods.Length; index++)
        {
            yield return candidate with
            {
                SupportingMethods = candidate.SupportingMethods.RemoveAt(index),
            };
        }
    }
}
