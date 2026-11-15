// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Text.Json;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class GeneratedCilManifestWriterTests
{
    [Fact]
    public void RetainsSeedStructureAndCompilerProvenance()
    {
        var path = Path.Combine(
            CorrectnessTestAssets.CreateDirectory(),
            "generated-cil.json");
        var artifact = new CorpusArtifact(
            "input.dll",
            "input.pdb",
            "assembly-sha",
            "pdb-sha",
            "compiler-version",
            ["-optimize+"]);

        new GeneratedCilManifestWriter().Write(
            path,
            GeneratedCilReductionTestPrograms.StraightLine().Program,
            artifact: artifact);

        var text = File.ReadAllText(path);
        Assert.Contains('\n', text);
        Assert.DoesNotContain('\r', text);
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("GeneratorVersion").GetInt32());
        Assert.Equal(17, root.GetProperty("Seed").GetInt32());
        Assert.Equal(
            "compiler-version",
            root.GetProperty("CompilerVersion").GetString());
        Assert.NotEmpty(root.GetProperty("Blocks").EnumerateArray());
    }
}
