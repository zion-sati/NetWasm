using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.Tests.Correctness;

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

    // A generated program branches to its next block with every conditional branch, the
    // shape a Debug build emits for an if whose body is a removed call (issue 221); its own
    // IL runs the same on NetWasm as on desktop .NET.
    [Fact]
    public void GeneratedProgramsRunTheirOwnIlAsDesktopDoes()
    {
        using var services = CorrectnessTestAssets.CreateServices();
        var generator = services.GetRequiredService<IRandomCilGenerator>();
        var serializer = services.GetRequiredService<IGeneratedCilSerializer>();
        var runner = services.GetRequiredService<IGeneratedCilRegressionRunner>();
        foreach (var seed in RandomCilFixtureFactory.PilotSeeds.Take(2))
        {
            runner.RunRaw(seed, serializer.Serialize(generator.Generate(seed)), RandomCilFixtureFactory.Create(seed).Inputs);
        }
    }
}
