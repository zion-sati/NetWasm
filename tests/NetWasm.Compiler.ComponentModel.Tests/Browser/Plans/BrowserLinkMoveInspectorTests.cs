// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel.Browser.Plans;

namespace NetWasm.Compiler.ComponentModel.Tests.Browser.Plans;

public sealed class BrowserLinkMoveInspectorTests
{
    [Fact]
    public void PublicationConsumesTheSourceAndPreservesValidation()
    {
        var result = Create().Inspect(new BrowserLinkMoveOperation("source", "output"));
        Assert.Equal("move", result.Capability);
        Assert.Equal(["source"], result.Reads);
        Assert.Equal(["output"], result.Writes);
        Assert.Equal(["source"], result.Consumes);
        Assert.True(result.PreservesValidation);
        Assert.Throws<ArgumentException>(() => Create().Inspect(new BrowserLinkMoveOperation("same", "same")));
        Assert.Throws<ArgumentException>(() => Create().Inspect(new BrowserLinkTextOperation("out", "text")));
        Assert.Throws<ArgumentException>(() => Create().Inspect(null!));
    }

    private static IBrowserLinkOperationInspector Create() => Assert.IsAssignableFrom<IBrowserLinkOperationInspector>(new BrowserLinkMoveInspector());
}
