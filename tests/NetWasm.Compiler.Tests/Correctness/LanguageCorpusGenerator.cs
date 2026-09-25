using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class LanguageCorpusGenerator(
    IPairwiseCoveringArray coveringArray,
    IEnumerable<IGeneratedCorpusTemplate> templates) : ILanguageCorpusGenerator
{
    private const int MaximumCases = 96;
    private const int MaximumSourceCharacters = 32 * 1024;
    private const int MaximumInputs = 8;

    public ImmutableArray<GeneratedCorpusCase> GeneratePullRequestCorpus()
    {
        var generated = ImmutableArray.CreateBuilder<GeneratedCorpusCase>();
        foreach (var template in templates.OrderBy(template => template.Family, StringComparer.Ordinal))
        {
            var combinations = coveringArray.Generate(template.Dimensions, template.Seed)
                .AddRange(template.TargetedCases)
                .DistinctBy(values => Signature(values, template.Dimensions), StringComparer.Ordinal)
                .ToImmutableArray();
            for (var index = 0; index < combinations.Length; index++)
            {
                var name = $"Generated{Sanitize(template.Family)}" +
                    $"{unchecked((uint)template.Seed):X8}{index:D2}";
                var fixture = template.Create(name, combinations[index]);
                ValidateBounds(fixture);
                generated.Add(new(
                    name,
                    template.Family,
                    template.Seed,
                    combinations[index],
                    fixture));
            }
        }
        if (generated.Count > MaximumCases)
        {
            throw new InvalidOperationException(
                $"generated pull-request corpus exceeded {MaximumCases} cases");
        }
        return generated.ToImmutable();
    }

    private static string Signature(
        ImmutableDictionary<string, string> values,
        ImmutableArray<CorpusDimension> dimensions) => string.Join(
        '|', dimensions.Select(dimension =>
            dimension.Name + '=' + values[dimension.Name]));

    private static string Sanitize(string value) => new(
        value.Where(char.IsAsciiLetterOrDigit).ToArray());

    private static void ValidateBounds(CorpusFixture fixture)
    {
        if (fixture.Source.Length > MaximumSourceCharacters)
        {
            throw new InvalidOperationException(
                $"{fixture.Name} exceeds the generated-source bound");
        }
        if (fixture.Inputs.IsDefaultOrEmpty || fixture.Inputs.Length > MaximumInputs)
        {
            throw new InvalidOperationException(
                $"{fixture.Name} has an invalid generated-input bound");
        }
    }
}
