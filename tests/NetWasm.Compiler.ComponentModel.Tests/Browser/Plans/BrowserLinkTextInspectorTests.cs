// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel.Browser.Plans;

namespace NetWasm.Compiler.ComponentModel.Tests.Browser.Plans;

public sealed class BrowserLinkTextInspectorTests
{
    [Fact]
    public void TextCreationWritesOnlyItsOutput()
    {
        var result = Create().Inspect(new BrowserLinkTextOperation("output", "(module)"));
        Assert.Equal("text", result.Capability);
        Assert.Empty(result.Reads);
        Assert.Equal(["output"], result.Writes);
        Assert.Empty(result.Consumes);
        Assert.Throws<ArgumentException>(() => Create().Inspect(new BrowserLinkMoveOperation("in", "out")));
        Assert.Throws<ArgumentException>(() => Create().Inspect(null!));
        Assert.Throws<ArgumentException>(() => Create().Inspect(new BrowserLinkTextOperation("output", " ")));
    }

    private static IBrowserLinkOperationInspector Create() => Assert.IsAssignableFrom<IBrowserLinkOperationInspector>(new BrowserLinkTextInspector());
}
