// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class GeneratedCilFailureBundleWriterTests
{
    private static readonly GeneratedCilFailureFingerprint Fingerprint = new(
        "semantic-mismatch",
        "wasm-execution",
        "return-value");

    [Fact]
    public void WritesHonestOriginalReducedAndCompleteReproductionEvidence()
    {
        using var services = CorrectnessTestAssets.CreateServices();
        var root = CorrectnessTestAssets.CreateDirectory();
        var evidence = Path.Combine(root, "evidence");
        Directory.CreateDirectory(evidence);
        var reducer = services.GetRequiredService<IGeneratedCilReducer>();
        var original = GeneratedCilReductionTestPrograms.StraightLine();
        var reduction = reducer.Reduce(new(
            original,
            Fingerprint,
            (_, _) => Fingerprint,
            TimeSpan.FromSeconds(5)));
        var originalAssembly = Write(evidence, "input.dll", "original-pe");
        var reducedAssembly = Write(evidence, "reduced-input.dll", "reduced-pe");
        var coreClrTrace = Write(evidence, "coreclr.json", "coreclr");
        var netWasmTrace = Write(evidence, "netwasm.json", "netwasm");
        var compilerTrace = Write(evidence, "compiler.txt", "compiler");
        var complexityMetrics = Write(evidence, "complexity.json", "complexity");
        var cfg = Write(evidence, "input.cfg", "cfg");
        var wasm = Write(evidence, "input.wasm", "wasm");
        var output = Path.Combine(root, "bundle");
        var artifact = new CorpusArtifact(
            originalAssembly,
            "input.pdb",
            "assembly-sha",
            "pdb-sha",
            "compiler-version",
            ["-optimize+"]);

        var actual = services
            .GetRequiredService<IGeneratedCilFailureBundleWriter>()
            .Write(new(
                output,
                reduction,
                artifact,
                originalAssembly,
                reducedAssembly,
                coreClrTrace,
                netWasmTrace,
                compilerTrace,
                complexityMetrics,
                cfg,
                wasm,
                "eng/replay-generated-cil.sh bundle"));

        Assert.Equal(output, actual);
        string[] expected =
        [
            "application.wasm",
            "cfg.txt",
            "compiler.trace.txt",
            "complexity-metrics.json",
            "coreclr.trace.json",
            "netwasm.trace.json",
            "original-generated-cil.json",
            "original.cil",
            "original.dll",
            "reduced-generated-cil.json",
            "reduced.cil",
            "reduced.dll",
            "reduction.json",
        ];
        Assert.Equal(
            expected,
            Directory.GetFiles(output).Select(Path.GetFileName).Order().ToArray());
        Assert.NotEqual(
            File.ReadAllBytes(Path.Combine(output, "original.cil")),
            File.ReadAllBytes(Path.Combine(output, "reduced.cil")));
        var record = File.ReadAllText(Path.Combine(output, "reduction.json"));
        Assert.Contains("compiler-version", record);
        Assert.Contains("eng/replay-generated-cil.sh bundle", record);
        Assert.Contains('\n', record);
        Assert.DoesNotContain('\r', record);
    }

    [Fact]
    public void RejectsAnUnchangedCandidateOrMissingEvidence()
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
        var artifact = new CorpusArtifact("missing", "missing", "a", "p", "v", []);
        var writer = services.GetRequiredService<IGeneratedCilFailureBundleWriter>();

        Assert.Throws<InvalidDataException>(() => writer.Write(new(
            CorrectnessTestAssets.CreateDirectory(),
            unchanged,
            artifact,
            "missing",
            "missing",
            "missing",
            "missing",
            "missing",
            "missing",
            "missing",
            "missing",
            "reproduce")));
    }

    private static string Write(string directory, string name, string contents)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, contents);
        return path;
    }
}
