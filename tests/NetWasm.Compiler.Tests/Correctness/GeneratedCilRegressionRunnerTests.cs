using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class GeneratedCilRegressionRunnerTests
{
    [Fact]
    public void ReplaysReducedPilotCilInBothRoslynProfiles()
    {
        using var services = CorrectnessTestAssets.CreateServices();
        var reduction = GeneratedCilRegressionPromoterTests.CreateReduction(services);
        var cil = services.GetRequiredService<IGeneratedCilSerializer>()
            .Serialize(reduction.Reduced.Program);

        Assert.Equal(
            NetWasm.Compiler.Core.CilOperation.Return,
            reduction.Reduced.Program.Blocks[^1].Instructions[^1].Operation);
        Assert.Equal(0x2a, cil[^1]);

        services.GetRequiredService<IGeneratedCilRegressionRunner>().RunRaw(
            reduction.Reduced.Program.Seed,
            cil,
            reduction.Reduced.Inputs);
    }
}
