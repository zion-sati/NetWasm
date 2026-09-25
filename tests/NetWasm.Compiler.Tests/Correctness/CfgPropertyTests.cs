namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class CfgPropertyTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void StructuredAndEmittedGraphsMatchTheReferenceInterpreter()
    {
        var requested = Environment.GetEnvironmentVariable(
            "NETWASM_CFG_PROPERTY_SEED");
        var seeds = requested is null
            ? (int[])[0x31415, 0x27182]
            : (int[])[int.Parse(
                requested,
                System.Globalization.CultureInfo.InvariantCulture)];
        foreach (var property in runner.GenerateCfgProperties(seeds))
        {
            runner.RunCfgProperty(property);
        }
    }
}
