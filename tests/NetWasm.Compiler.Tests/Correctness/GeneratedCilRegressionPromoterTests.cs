using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class GeneratedCilRegressionPromoterTests
{
    private static readonly GeneratedCilFailureFingerprint Fingerprint = new(
        "semantic-mismatch",
        "wasm-execution",
        "return-value");

    [Fact]
    public void CreatesACompilableFocusedRegressionShapeAndReplayInputs()
    {
        using var services = CorrectnessTestAssets.CreateServices();
        var reduction = CreateReduction(services);
        var output = CorrectnessTestAssets.CreateDirectory();

        var sourcePath = services
            .GetRequiredService<IGeneratedCilRegressionPromoter>()
            .Promote("CheckedMultiplySeed", output, reduction);

        Assert.True(File.Exists(sourcePath));
        Assert.True(File.Exists(Path.Combine(output, "CheckedMultiplySeed.cil")));
        Assert.True(File.Exists(Path.Combine(output, "CheckedMultiplySeed.json")));
        var source = File.ReadAllText(sourcePath);
        Assert.Contains("public sealed class CheckedMultiplySeedRegressionTests", source);
        Assert.Contains("IGeneratedCilRegressionRunner", source);
        Assert.Contains("Convert.FromBase64String", source);
        Assert.Contains("19", source);
        Assert.Contains('\n', source);
        Assert.DoesNotContain('\r', source);
    }

    [Fact]
    public void RejectsUnsafeIdentifiersAndUnchangedCases()
    {
        using var services = CorrectnessTestAssets.CreateServices();
        var original = GeneratedCilReductionTestPrograms.StraightLine();
        var unchanged = new GeneratedCilReductionResult(
            original,
            original,
            Fingerprint,
            false,
            0,
            []);
        var promoter = services.GetRequiredService<IGeneratedCilRegressionPromoter>();

        Assert.Throws<ArgumentException>(() => promoter.Promote(
            "not-valid!",
            CorrectnessTestAssets.CreateDirectory(),
            unchanged));
        Assert.Throws<InvalidDataException>(() => promoter.Promote(
            "ValidId",
            CorrectnessTestAssets.CreateDirectory(),
            unchanged));
    }

    internal static GeneratedCilReductionResult CreateReduction(
        ServiceProvider services)
    {
        var generator = new RandomCilGenerator();
        var seed = RandomCilFixtureFactory.PilotSeeds.First(candidate =>
            generator.Generate(candidate).Operations.Contains(
                CilOperation.MultiplyCheckedUnsigned));
        var original = new GeneratedCilReductionCase(
            generator.Generate(seed),
            RandomCilFixtureFactory.Create(seed).Inputs);
        return services.GetRequiredService<IGeneratedCilReducer>().Reduce(new(
            original,
            Fingerprint,
            (candidate, _) => candidate.Inputs.Contains(19) &&
                              candidate.Program.Operations.Contains(
                                  CilOperation.MultiplyCheckedUnsigned)
                ? Fingerprint
                : null,
            TimeSpan.FromSeconds(10)));
    }
}
