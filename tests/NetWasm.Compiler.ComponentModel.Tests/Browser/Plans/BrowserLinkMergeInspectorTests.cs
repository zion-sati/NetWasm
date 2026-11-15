// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel.Browser.Plans;

namespace NetWasm.Compiler.ComponentModel.Tests.Browser.Plans;

public sealed class BrowserLinkMergeInspectorTests
{
    [Fact]
    public void MergeBindsPathsWithoutReinterpretingModuleIdentities()
    {
        var result = Create().Inspect(Operation());
        Assert.Equal("wasm-merge", result.ToolId);
        Assert.Equal([0, 2], result.InputArgumentIndices);
        Assert.Equal([5], result.OutputArgumentIndices);
        Assert.Equal(["first", "second"], result.Reads);
        Assert.Equal(["output"], result.Writes);
    }

    [Fact]
    public void RejectsInvalidOperandPairsBindingsAndFlags()
    {
        var operation = Operation();
        foreach (var invalid in new BrowserLinkOperation?[]
        {
            null, new BrowserLinkTextOperation("out", "text"), operation with { ToolId = "other" },
            operation with { Arguments = default }, operation with { InputPaths = default }, operation with { OutputPaths = default },
            operation with { Arguments = [] }, operation with { Arguments = ["first", "--output", "output"] },
            operation with { Arguments = ["first", "module", "--output"] },
            operation with { OutputPaths = [] }, operation with { OutputPaths = ["another"] },
            operation with { InputPaths = ["another"] },
            operation with { Arguments = ["first", " ", "second", "runtime", "--output", "output"] },
            operation with { Arguments = ["first", "-bad", "second", "runtime", "--output", "output"] },
            operation with { Arguments = ["first", "bad\nname", "second", "runtime", "--output", "output"] },
            operation with { Arguments = ["first", "same", "second", "same", "--output", "output"] },
            operation with { Arguments = [.. operation.Arguments, "--unknown"] },
            operation with { Arguments = [.. operation.Arguments, "--enable-memory64", "--enable-memory64"] },
        }) Assert.Throws<ArgumentException>(() => Create().Inspect(invalid!));
    }

    private static BrowserLinkToolOperation Operation() =>
        new("wasm-merge", ["first", "application", "second", "runtime", "--output", "output"], ["first", "second"], ["output"]);
    private static IBrowserLinkOperationInspector Create() => Assert.IsAssignableFrom<IBrowserLinkOperationInspector>(new BrowserLinkMergeInspector());
}
