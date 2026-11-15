// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class GeneratedFailureArtifactWriterTests
{
    [Fact]
    public void WriteCreatesHonestOriginalSourceDimensionsAndExactReproduction()
    {
        var environment = CompilerCorrectnessEnvironment.Discover();
        var subject = new GeneratedFailureArtifactWriter(environment);
        var generatedCase = new GeneratedCorpusCase(
            "GeneratedArtifactUnit",
            "Unit",
            77,
            ImmutableDictionary<string, string>.Empty.Add("loop", "do"),
            CorrectnessTestAssets.CreateFixture("GeneratedArtifactUnit"));

        var directory = subject.Write(generatedCase, new InvalidOperationException("broken"));

        Assert.Equal(generatedCase.Fixture.Source,
            File.ReadAllText(Path.Combine(directory, "original.cs")));
        Assert.False(File.Exists(Path.Combine(directory, "reduced.cs")));
        var json = File.ReadAllText(Path.Combine(directory, "failure.json"));
        Assert.Contains("\"Seed\": 77", json, StringComparison.Ordinal);
        Assert.Contains("\"loop\": \"do\"", json, StringComparison.Ordinal);
        Assert.Contains("NETWASM_GENERATED_CASE=GeneratedArtifactUnit", json,
            StringComparison.Ordinal);
        Assert.Contains("broken", json, StringComparison.Ordinal);
        Assert.Contains('\n', json);
        Assert.DoesNotContain('\r', json);
    }
}
