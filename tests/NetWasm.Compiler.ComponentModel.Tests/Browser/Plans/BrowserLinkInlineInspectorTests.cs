// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel.Browser.Plans;
using NetWasm.Compiler.ComponentModel.Inlining;

namespace NetWasm.Compiler.ComponentModel.Tests.Browser.Plans;

public sealed class BrowserLinkInlineInspectorTests
{
    [Theory]
    [InlineData(WasmInlineAnnotationSelection.Preserve)]
    [InlineData(WasmInlineAnnotationSelection.SizeAware)]
    [InlineData(WasmInlineAnnotationSelection.Remove)]
    public void CustomSectionOnlyEditsPreservePriorValidation(WasmInlineAnnotationSelection selection)
    {
        var result = Create().Inspect(new BrowserLinkInlineOperation("input", "output", selection));
        Assert.Equal("inline", result.Capability);
        Assert.Equal(["input"], result.Reads);
        Assert.Equal(["output"], result.Writes);
        Assert.Empty(result.Consumes);
        Assert.True(result.PreservesValidation);
    }

    [Fact]
    public void RejectsUnknownSelectionAndWrongOperation()
    {
        Assert.Throws<ArgumentException>(() => Create().Inspect(new BrowserLinkInlineOperation("in", "out", (WasmInlineAnnotationSelection)99)));
        Assert.Throws<ArgumentException>(() => Create().Inspect(new BrowserLinkTextOperation("out", "text")));
        Assert.Throws<ArgumentException>(() => Create().Inspect(null!));
    }

    private static IBrowserLinkOperationInspector Create() => Assert.IsAssignableFrom<IBrowserLinkOperationInspector>(new BrowserLinkInlineInspector());
}
