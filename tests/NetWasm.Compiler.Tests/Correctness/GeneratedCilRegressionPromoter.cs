using System.Text;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class GeneratedCilRegressionPromoter(
    IGeneratedCilSerializer serializer,
    IGeneratedCilManifestWriter manifests) : IGeneratedCilRegressionPromoter
{
    public string Promote(
        string regressionId,
        string outputDirectory,
        GeneratedCilReductionResult reduction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(regressionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(reduction);
        if (!regressionId.All(character =>
                char.IsAsciiLetterOrDigit(character) || character == '_'))
        {
            throw new ArgumentException(
                "regression IDs may contain only ASCII letters, digits and underscore",
                nameof(regressionId));
        }
        if (GeneratedCilReductionScore.Measure(reduction.Reduced)
                .CompareTo(GeneratedCilReductionScore.Measure(reduction.Original)) >= 0)
        {
            throw new InvalidDataException(
                "an unchanged generated case cannot be promoted as reduced");
        }
        if (!reduction.Reduced.SupportingMethods.IsEmpty)
        {
            throw new InvalidDataException(
                "supporting methods must be reduced or materialized before promotion");
        }
        Directory.CreateDirectory(outputDirectory);
        var cil = serializer.Serialize(reduction.Reduced.Program);
        var cilPath = Path.Combine(outputDirectory, regressionId + ".cil");
        File.WriteAllBytes(cilPath, cil);
        manifests.Write(
            Path.Combine(outputDirectory, regressionId + ".json"),
            reduction.Reduced.Program);
        var sourcePath = Path.Combine(
            outputDirectory,
            regressionId + "RegressionTests.cs");
        File.WriteAllText(
            sourcePath,
            CreateSource(regressionId, reduction, cil),
            new UTF8Encoding(false));
        return sourcePath;
    }

    private static string CreateSource(
        string regressionId,
        GeneratedCilReductionResult reduction,
        byte[] cil)
    {
        var inputs = string.Join(
            ", ",
            reduction.Reduced.Inputs.Select(input => input.ToString(
                System.Globalization.CultureInfo.InvariantCulture)));
        return ($$"""
            using Microsoft.Extensions.DependencyInjection;

            namespace NetWasm.Compiler.Tests.Correctness;

            public sealed class {{regressionId}}RegressionTests
            {
                [Fact]
                public void ReplaysReducedCilAgainstCoreClr()
                {
                    using var services = CorrectnessTestAssets.CreateServices();
                    services.GetRequiredService<IGeneratedCilRegressionRunner>().RunRaw(
                        {{reduction.Reduced.Program.Seed}},
                        Convert.FromBase64String("{{Convert.ToBase64String(cil)}}"),
                        [{{inputs}}]);
                }
            }
            """ + "\n").ReplaceLineEndings("\n");
    }
}
