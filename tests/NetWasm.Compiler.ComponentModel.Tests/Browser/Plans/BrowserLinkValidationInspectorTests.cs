// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel.Browser.Plans;

namespace NetWasm.Compiler.ComponentModel.Tests.Browser.Plans;

public sealed class BrowserLinkValidationInspectorTests
{
    [Fact]
    public void ValidationBindsOnlyTheInputAndPreservesFeatureArguments()
    {
        var operation = new BrowserLinkValidationOperation("module", ["validate", "module", "--features", "mvp,memory64"]);
        var result = Create().Inspect(operation);
        Assert.Equal("validate", result.Capability);
        Assert.Equal("wasm-tools", result.ToolId);
        Assert.Equal(operation.Arguments, result.Arguments);
        Assert.Equal(["module"], result.Reads);
        Assert.Empty(result.Writes);
        Assert.Empty(result.Consumes);
        Assert.True(result.ValidatesInputs);
        Assert.Equal([1], result.InputArgumentIndices);
    }

    [Fact]
    public void RejectsContradictoryCommandsAndUnknownOrRepeatedFeatures()
    {
        var operation = new BrowserLinkValidationOperation("module", ["validate", "module", "--features", "mvp"]);
        foreach (var invalid in new BrowserLinkOperation?[]
        {
            null, new BrowserLinkTextOperation("out", "text"),
            operation with { Arguments = default }, operation with { Arguments = [] },
            operation with { Arguments = ["parse", "module", "--features", "mvp"] },
            operation with { Arguments = ["validate", "another", "--features", "mvp"] },
            operation with { Arguments = ["validate", "module", "--other", "mvp"] },
            operation with { Arguments = ["validate", "module", "--features", " "] },
            operation with { Arguments = ["validate", "module", "--features", "unsupported"] },
            operation with { Arguments = ["validate", "module", "--features", "mvp,mvp"] },
        }) Assert.Throws<ArgumentException>(() => Create().Inspect(invalid!));
    }

    private static IBrowserLinkOperationInspector Create() => Assert.IsAssignableFrom<IBrowserLinkOperationInspector>(new BrowserLinkValidationInspector());
}
