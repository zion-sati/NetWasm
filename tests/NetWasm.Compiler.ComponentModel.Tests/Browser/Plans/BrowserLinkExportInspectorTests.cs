// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel.Browser.Plans;

namespace NetWasm.Compiler.ComponentModel.Tests.Browser.Plans;

public sealed class BrowserLinkExportInspectorTests
{
    [Fact]
    public void DescribesComponentRetentionAndRawRemovalWithoutPreservingValidation()
    {
        foreach (var operation in new BrowserLinkExportOperation[]
        {
            new("input", "output", "cm32p2", []), new("input", "output", null, []),
            new("input", "output", null, [new("temporary", 0), new("memory", 2)]),
        })
        {
            var result = Create().Inspect(operation);
            Assert.Equal("exports", result.Capability);
            Assert.Equal(["input"], result.Reads);
            Assert.Equal(["output"], result.Writes);
            Assert.Empty(result.Consumes);
            Assert.False(result.PreservesValidation);
        }
    }

    [Fact]
    public void RejectsAmbiguousOrUninitializedSelectionsAndInvalidExportIdentities()
    {
        foreach (var operation in new BrowserLinkOperation?[]
        {
            null, new BrowserLinkTextOperation("out", "text"),
            new BrowserLinkExportOperation("in", "out", null, default),
            new BrowserLinkExportOperation("in", "out", " ", []),
            new BrowserLinkExportOperation("in", "out", "prefix", [new("export", 0)]),
            new BrowserLinkExportOperation("in", "out", null, [null!]),
            new BrowserLinkExportOperation("in", "out", null, [new(" ", 0)]),
            new BrowserLinkExportOperation("in", "out", null, [new("name", 5)]),
            new BrowserLinkExportOperation("in", "out", null, [new("same", 0), new("same", 1)]),
        }) Assert.Throws<ArgumentException>(() => Create().Inspect(operation!));
    }

    private static IBrowserLinkOperationInspector Create() => Assert.IsAssignableFrom<IBrowserLinkOperationInspector>(new BrowserLinkExportInspector());
}
