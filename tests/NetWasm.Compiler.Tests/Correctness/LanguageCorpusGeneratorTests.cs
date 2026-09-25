using Microsoft.Extensions.DependencyInjection;
using Microsoft.CodeAnalysis.CSharp;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class LanguageCorpusGeneratorTests
{
    [Fact]
    public void PullRequestCorpusIsDeterministicBoundedAndPairwiseComplete()
    {
        using var services = CorrectnessTestAssets.CreateServices();
        var subject = services.GetRequiredService<ILanguageCorpusGenerator>();
        var templates = services.GetServices<IGeneratedCorpusTemplate>()
            .ToDictionary(template => template.Family, StringComparer.Ordinal);

        var first = subject.GeneratePullRequestCorpus();
        var second = subject.GeneratePullRequestCorpus();

        Assert.Equal(
            first.Select(Describe),
            second.Select(Describe));
        Assert.Equal(first.Length, first.Select(item => item.Name)
            .Distinct(StringComparer.Ordinal).Count());
        Assert.InRange(first.Length, 1, 96);
        Assert.Equal(templates.Keys.Order(StringComparer.Ordinal),
            first.Select(item => item.Family).Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal));
        foreach (var generatedCase in first)
        {
            Assert.Equal(generatedCase.Name, generatedCase.Fixture.Name);
            Assert.InRange(generatedCase.Fixture.Source.Length, 1, 32 * 1024);
            Assert.InRange(generatedCase.Fixture.Inputs.Length, 1, 8);
        }
        foreach (var template in templates.Values)
        {
            var cases = first.Where(item => item.Family == template.Family).ToArray();
            AssertPairs(template, cases);
            foreach (var targeted in template.TargetedCases)
            {
                Assert.Contains(cases, generatedCase => template.Dimensions.All(dimension =>
                    generatedCase.Dimensions[dimension.Name] == targeted[dimension.Name]));
            }
        }
    }

    [Fact]
    public void AsyncCleanupCorpusCoversEverySuspensionCleanupGcTriple()
    {
        using var services = CorrectnessTestAssets.CreateServices();
        var template = Assert.IsType<AsyncCleanupCorpusTemplate>(services
            .GetServices<IGeneratedCorpusTemplate>()
            .Single(item => item.Family == "AsyncCleanup"));
        var cases = services.GetRequiredService<ILanguageCorpusGenerator>()
            .GeneratePullRequestCorpus()
            .Where(item => item.Family == template.Family)
            .ToArray();
        var suspension = template.Dimensions.Single(item =>
            item.Name == "suspension");
        var cleanup = template.Dimensions.Single(item => item.Name == "cleanup");
        var gc = template.Dimensions.Single(item => item.Name == "gc");

        foreach (var suspensionLevel in suspension.Levels)
            foreach (var cleanupLevel in cleanup.Levels)
                foreach (var gcLevel in gc.Levels)
                {
                    Assert.Contains(cases, item =>
                        item.Dimensions["suspension"] == suspensionLevel &&
                        item.Dimensions["cleanup"] == cleanupLevel &&
                        item.Dimensions["gc"] == gcLevel);
                }
    }

    [Fact]
    public void SyntaxTreeProfilesCoverEveryAdvancedLanguageFamily()
    {
        using var services = CorrectnessTestAssets.CreateServices();
        var cases = services.GetRequiredService<ILanguageCorpusGenerator>()
            .GeneratePullRequestCorpus()
            .Where(item => item.Family == "SyntaxTreeProfiles")
            .ToArray();

        Assert.True(SyntaxTreeProfileCorpusTemplate.FeatureManifest.AsSpan().SequenceEqual(
            ["arithmetic", "async", "control-flow", "eh", "iterator", "mixed", "object-model"]));
        Assert.Equal(SyntaxTreeProfileCorpusTemplate.FeatureManifest.Length, cases.Length);
        Assert.All(cases, item => Assert.DoesNotContain(
            CSharpSyntaxTree.ParseText(item.Fixture.Source).GetDiagnostics(),
            diagnostic => diagnostic.Severity ==
                global::Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
    }

    [Fact]
    public void SyntaxTreeProfilesInitializeTheirOptionalTraceState()
    {
        using var services = CorrectnessTestAssets.CreateServices();
        var cases = services.GetRequiredService<ILanguageCorpusGenerator>()
            .GeneratePullRequestCorpus()
            .Where(item => item.Family == "SyntaxTreeProfiles")
            .ToArray();

        Assert.All(cases, item =>
        {
            var root = CSharpSyntaxTree.ParseText(item.Fixture.Source).GetRoot();
            var trace = root.DescendantNodes()
                .OfType<global::Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclaratorSyntax>()
                .Single(variable => variable.Identifier.ValueText == "_trace");
            Assert.NotNull(trace.Initializer);
        });
    }

    private static string Describe(GeneratedCorpusCase generatedCase) =>
        generatedCase.Name + '|' + generatedCase.Seed + '|' +
        string.Join('|', generatedCase.Dimensions
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => item.Key + '=' + item.Value)) + '|' +
        generatedCase.Fixture.Source;

    private static void AssertPairs(
        IGeneratedCorpusTemplate template,
        IReadOnlyCollection<GeneratedCorpusCase> cases)
    {
        for (var left = 0; left < template.Dimensions.Length; left++)
        {
            for (var right = left + 1; right < template.Dimensions.Length; right++)
            {
                var leftDimension = template.Dimensions[left];
                var rightDimension = template.Dimensions[right];
                foreach (var leftLevel in leftDimension.Levels)
                {
                    foreach (var rightLevel in rightDimension.Levels)
                    {
                        Assert.Contains(cases, generatedCase =>
                            generatedCase.Dimensions[leftDimension.Name] == leftLevel &&
                            generatedCase.Dimensions[rightDimension.Name] == rightLevel);
                    }
                }
            }
        }
    }
}
