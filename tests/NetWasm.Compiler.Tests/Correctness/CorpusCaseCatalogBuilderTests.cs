using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class CorpusCaseCatalogBuilderTests
{
    [Fact]
    public void BuildsAnImmutableCaseKeyedCatalogAndAllowsSharedSourceAssets()
    {
        var first = CorpusCaseContractData.Manifest;
        var second = first with { CaseId = "numeric.other", TestMethod = "Tests.Numeric.Other" };
        var boundary = new Boundaries(first, second);
        var assets = Assets() with { Manifests = [new("first.case.json", "first"), new("second.case.json", "second")] };

        var catalog = Builder(boundary).Build(assets, [Binding(first), Binding(second)]);

        Assert.Equal(2, catalog.Cases.Count);
        Assert.Same(first, catalog.Cases[first.CaseId]);
        Assert.Same(second, catalog.Cases[second.CaseId]);
        Assert.Equal(["parse:first", "verify:numeric.case", "parse:second", "verify:numeric.other"], boundary.Calls);
        Assert.Throws<KeyNotFoundException>(() => catalog.Cases["NUMERIC.CASE"]);
    }

    [Theory]
    [InlineData("no-manifests")]
    [InlineData("default-manifests")]
    [InlineData("default-sources")]
    [InlineData("null-source")]
    [InlineData("empty-source")]
    [InlineData("duplicate-source")]
    [InlineData("null-manifest")]
    [InlineData("empty-manifest-name")]
    [InlineData("duplicate-manifest-name")]
    [InlineData("duplicate-case-id")]
    [InlineData("missing-source")]
    [InlineData("wrong-source-case")]
    [InlineData("orphan-source")]
    public void RejectsInvalidOrOrphanAssets(string defect)
    {
        var assets = defect switch
        {
            "no-manifests" => Assets() with { Manifests = [] },
            "default-manifests" => Assets() with { Manifests = default },
            "default-sources" => Assets() with { SourceFiles = default },
            "null-source" => Assets() with { SourceFiles = [null!] },
            "empty-source" => Assets() with { SourceFiles = [""] },
            "duplicate-source" => Assets() with { SourceFiles = ["Entry.cs", "Entry.cs"] },
            "null-manifest" => Assets() with { Manifests = [null!] },
            "empty-manifest-name" => Assets() with { Manifests = [new("", "first")] },
            "duplicate-manifest-name" => Assets() with { Manifests = [new("first.case.json", "first"), new("first.case.json", "second")] },
            "duplicate-case-id" => Assets() with { Manifests = [new("first.case.json", "first"), new("second.case.json", "second")] },
            "missing-source" => Assets() with { SourceFiles = [] },
            "wrong-source-case" => Assets() with { SourceFiles = ["entry.cs"] },
            "orphan-source" => Assets() with { SourceFiles = ["Entry.cs", "Orphan.cs"] },
            _ => throw new ArgumentOutOfRangeException(nameof(defect)),
        };
        var boundary = new Boundaries(CorpusCaseContractData.Manifest, CorpusCaseContractData.Manifest);

        Assert.Throws<ArgumentException>(() => Builder(boundary).Build(assets, [Binding(CorpusCaseContractData.Manifest)]));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("default")]
    [InlineData("null")]
    [InlineData("empty-id")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("method")]
    [InlineData("kind")]
    [InlineData("orphan")]
    public void RejectsInvalidOrOrphanBindings(string defect)
    {
        var original = Binding(CorpusCaseContractData.Manifest);
        ImmutableArray<CorpusCaseBinding> bindings = defect switch
        {
            "empty" => [],
            "default" => default,
            "null" => [null!],
            "empty-id" => [original with { CaseId = " " }],
            "duplicate" => [original, original],
            "missing" => [original with { CaseId = "numeric.other" }],
            "method" => [original with { TestMethod = "Tests.Other.Run" }],
            "kind" => [original with { InputKind = CorpusInputKind.Emitted }],
            "orphan" => [original, original with { CaseId = "numeric.other" }],
            _ => throw new ArgumentOutOfRangeException(nameof(defect)),
        };
        var boundary = new Boundaries(CorpusCaseContractData.Manifest);

        Assert.Throws<ArgumentException>(() => Builder(boundary).Build(Assets(), bindings));
    }

    [Fact]
    public void RejectsNullBeforeParsing()
    {
        var boundary = new Boundaries(CorpusCaseContractData.Manifest);
        Assert.Throws<ArgumentNullException>(() => Builder(boundary).Build(null!, []));
        Assert.Empty(boundary.Calls);
    }

    [Theory]
    [InlineData("parse:first", "parse:first")]
    [InlineData("verify:numeric.case", "parse:first,verify:numeric.case")]
    public void RetainsFirstFailureWithoutReturningAPartialCatalog(string failing, string expectedCalls)
    {
        var boundary = new Boundaries(CorpusCaseContractData.Manifest) { Failing = failing };
        var assets = Assets() with { Manifests = [new("first.case.json", "first"), new("second.case.json", "second")] };

        var failure = Assert.Throws<InvalidOperationException>(() => Builder(boundary).Build(assets, [Binding(CorpusCaseContractData.Manifest)]));

        Assert.Same(boundary.Failure, failure);
        Assert.Equal(expectedCalls.Split(','), boundary.Calls);
    }

    [Fact]
    public void EmittedCatalogDoesNotInventSourceAssets()
    {
        var manifest = CorpusCaseContractData.Manifest with { InputKind = CorpusInputKind.Emitted, SourceFiles = [] };
        var boundary = new Boundaries(manifest);

        var catalog = Builder(boundary).Build(Assets() with { SourceFiles = [] }, [Binding(manifest)]);

        Assert.Same(manifest, Assert.Single(catalog.Cases).Value);
        Assert.Empty(manifest.SourceFiles);
        Assert.Equal(["parse:first", "verify:numeric.case"], boundary.Calls);
    }

    private static CorpusCaseAssets Assets() => new(new(ImmutableHashSet.Create("S06")), [new("first.case.json", "first")], ["Entry.cs"]);

    private static CorpusCaseBinding Binding(CorpusCaseManifest manifest) => new(manifest.CaseId, manifest.TestMethod, manifest.InputKind);

    private static ICorpusCaseCatalogBuilder Builder(Boundaries boundaries) =>
        Assert.IsAssignableFrom<ICorpusCaseCatalogBuilder>(new CorpusCaseCatalogBuilder(boundaries, boundaries));

    private sealed class Boundaries(params CorpusCaseManifest[] manifests) : ICorpusCaseManifestParser, ICorpusCaseManifestVerifier
    {
        private int _next;
        public List<string> Calls { get; } = [];
        public string? Failing { get; init; }
        public InvalidOperationException Failure { get; } = new("catalog-boundary-failure");

        public CorpusCaseManifest Parse(string json)
        {
            Record("parse:" + json);
            return manifests[_next++];
        }

        public void Verify(CorpusCaseManifest manifest) => Record("verify:" + manifest.CaseId);

        private void Record(string call)
        {
            Calls.Add(call);
            if (call == Failing)
            {
                throw Failure;
            }
        }
    }
}

internal static class CorpusCaseContractData
{
    public static CorpusCaseManifest Manifest => new(
        1, "numeric.case", ["S06"], CorpusInputKind.CSharp, ["Entry.cs"],
        "NumericCase", "Tests.Numeric", "Invoke", [0, 1, 2],
        [new(0, 42, null), new(1, null, "System.OverflowException")], ["Support.dll"],
        OracleMode.SameIl, CorpusMatrixProfile.Extended, "Tests.Numeric.Run");
}
