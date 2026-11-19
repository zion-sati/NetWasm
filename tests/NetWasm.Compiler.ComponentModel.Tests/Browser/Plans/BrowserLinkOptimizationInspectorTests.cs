// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel.Browser.Plans;

namespace NetWasm.Compiler.ComponentModel.Tests.Browser.Plans;

public sealed class BrowserLinkOptimizationInspectorTests
{
    [Theory]
    [InlineData("-O0")]
    [InlineData("-O1")]
    [InlineData("-O2")]
    [InlineData("-O3")]
    [InlineData("-Os")]
    [InlineData("-Oz")]
    public void DescribesExactlyTheDeclaredArtifactEffectsWithoutSelectingPolicy(string profile)
    {
        var operation = Operation() with
        {
            Arguments = ["input", profile, "--converge", "--enable-memory64", "--low-memory-unused",
                "--one-caller-inline-max-function-size", "1000", "--output", "output"],
        };

        var contract = Create().Inspect(operation);

        Assert.Equal("tool", contract.Capability);
        Assert.Equal(operation.InputPaths, contract.Reads);
        Assert.Equal(operation.OutputPaths, contract.Writes);
        Assert.Empty(contract.Consumes);
        Assert.False(contract.PreservesValidation);
        Assert.False(contract.ValidatesInputs);
    }

    [Fact]
    public void RejectsUnknownFlagsConflictingProfilesAndMismatchedArtifactDeclarations()
    {
        var operation = Operation();
        foreach (var invalid in new[]
        {
            operation with { ToolId = "arbitrary" },
            operation with { Arguments = default },
            operation with { InputPaths = default },
            operation with { OutputPaths = default },
            operation with { Arguments = [] },
            operation with { Arguments = ["input", "-O3", "-o", "output"] },
            operation with { InputPaths = [] },
            operation with { InputPaths = ["another"] },
            operation with { OutputPaths = [] },
            operation with { OutputPaths = ["another"] },
            operation with { Arguments = ["input", "--converge", "--output", "output"] },
            operation with { Arguments = ["input", "-O3", "-Oz", "--output", "output"] },
            operation with { Arguments = ["input", "-O3", "-O3", "--output", "output"] },
            operation with { Arguments = ["input", "-O3", "--unknown", "--output", "output"] },
            // A counted option needs its count before --output: a positive decimal number as the compiler writes it.
            operation with { Arguments = ["input", "-O3", "--one-caller-inline-max-function-size", "--output", "output"] },
            operation with { Arguments = ["input", "-O3", "--one-caller-inline-max-function-size", "0", "--output", "output"] },
            operation with { Arguments = ["input", "-O3", "--one-caller-inline-max-function-size", "01", "--output", "output"] },
            operation with { Arguments = ["input", "-O3", "--one-caller-inline-max-function-size", "-1", "--output", "output"] },
            operation with { Arguments = ["input", "-O3", "--one-caller-inline-max-function-size", "1e3", "--output", "output"] },
            operation with { Arguments = ["input", "-O3", "--one-caller-inline-max-function-size", "", "--output", "output"] },
            operation with { Arguments = ["input", "-O3", "--one-caller-inline-max-function-size", "1000000000", "--output", "output"] },
            operation with { Arguments = ["input", "-O3", "--one-caller-inline-max-function-size", "1000",
                "--one-caller-inline-max-function-size", "1000", "--output", "output"] },
        }) Assert.Throws<ArgumentException>(() => Create().Inspect(invalid));
        Assert.Throws<ArgumentException>(() => Create().Inspect(new BrowserLinkTextOperation("output", "text")));
        Assert.Throws<ArgumentException>(() => Create().Inspect(null!));
    }

    private static BrowserLinkToolOperation Operation() =>
        new("wasm-opt", ["input", "-O3", "--output", "output"], ["input"], ["output"]);

    private static IBrowserLinkOperationInspector Create() =>
        Assert.IsAssignableFrom<IBrowserLinkOperationInspector>(new BrowserLinkOptimizationInspector());
}
