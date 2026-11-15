// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Hosting.Build.JavaScript;

namespace NetWasm.Hosting.Build.Tests;

public sealed class CommandRunModuleWriterTests
{
    [Fact]
    public void WritesARunModulePinnedToTheDeploymentWithTheBuildConfiguration()
    {
        using var directory = TemporaryDirectory.Create();
        var output = Path.Combine(directory.Path, "App.mjs");
        var declaration = Path.Combine(directory.Path, "App.d.mts");
        var writer = Assert.IsAssignableFrom<ICommandRunModuleWriter>(new CommandRunModuleWriter());

        writer.Write(Valid(output, declaration));

        var source = File.ReadAllText(output);
        Assert.StartsWith("import { runNetWasm } from \"./netwasm.browser.mjs\";", source, StringComparison.Ordinal);
        Assert.Contains($"  buildFingerprint: \"{new string('1', 64)}\",", source, StringComparison.Ordinal);
        Assert.Contains($"  manifestSha256: \"{new string('2', 64)}\",", source, StringComparison.Ordinal);
        Assert.Contains("  arguments: Object.freeze([\"App\",\"--verbose\"]),", source, StringComparison.Ordinal);
        Assert.Contains("    Object.freeze({ name: \"MODE\", value: \"page\" }),", source, StringComparison.Ordinal);
        Assert.Contains("    network: \"denyAll\",", source, StringComparison.Ordinal);
        Assert.Contains("  return runNetWasm(Object.freeze({ configuration, options, startup }));", source,
            StringComparison.Ordinal);
        var types = File.ReadAllText(declaration);
        Assert.Contains("export declare function run(options?: NetWasmRunOptions): Promise<NetWasmExecutionResult>;",
            types, StringComparison.Ordinal);
        Assert.Contains("  readonly detail?: string;", types, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', source + types);
    }

    [Fact]
    public void RejectsInvalidPublicationContracts()
    {
        using var directory = TemporaryDirectory.Create();
        var valid = Valid(Path.Combine(directory.Path, "App.mjs"), Path.Combine(directory.Path, "App.d.mts"));
        var writer = new CommandRunModuleWriter();
        foreach (var request in new[]
                 {
                     valid with { OutputPath = "App.mjs" },
                     valid with { DeclarationPath = " " },
                     valid with { BundleFileName = "../netwasm.browser.mjs" },
                     valid with { BundleFileName = "" },
                     valid with { BuildFingerprint = "not-a-digest" },
                     valid with { ManifestSha256 = new string('A', 64) },
                     valid with { Arguments = [] },
                     valid with { Arguments = ["App\0"] },
                     valid with { Environment = default },
                     valid with { Environment = [new(" ", "value")] },
                     valid with { Environment = [new("A\0", "value")] },
                     valid with { Environment = [new("A", "value\0")] },
                     valid with { Environment = [new("A", "1"), new("A", "2")] },
                     valid with { Clocks = default },
                     valid with { Clocks = ["cpu"] },
                     valid with { Clocks = ["wall", "wall"] },
                     valid with { Network = "some" },
                 })
        {
            Assert.Throws<ArgumentException>(() => writer.Write(request));
        }
        Assert.Throws<ArgumentNullException>(() => writer.Write(null!));
    }

    private static CommandRunModuleRequest Valid(string output, string declaration) => new(
        "netwasm.browser.mjs",
        new string('1', 64),
        new string('2', 64),
        output,
        declaration,
        ["App", "--verbose"],
        [new("MODE", "page")],
        ["wall", "monotonic"],
        "denyAll",
        Randomness: true);
}
