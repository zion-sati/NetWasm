// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel.Browser.Plans;
using NetWasm.Compiler.ComponentModel.Inlining;

namespace NetWasm.Compiler.ComponentModel.Tests.Browser.Plans;

public sealed class BrowserLinkOperationRecorderTests
{
    [Fact]
    public void RecordsAvailableArtifactsInProducerOrderWithoutStageAssumptions()
    {
        var state = State();
        var recorder = Create(state);
        var inline = new BrowserLinkInlineOperation("input", "temporary", WasmInlineAnnotationSelection.Preserve);
        var text = new BrowserLinkTextOperation("text", "(module)");
        var move = new BrowserLinkMoveOperation("temporary", "output");
        recorder.Record(inline);
        recorder.Record(text);
        recorder.Record(move);
        Assert.Equal([inline, text, move], state.Operations);
        Assert.True(state.Available.SetEquals(["input", "text", "output"]));
    }

    [Fact]
    public void InvalidReadsAndWritesLeaveCapturedStateUntouched()
    {
        var state = State();
        var recorder = Create(state);
        Assert.Throws<ArgumentNullException>(() => recorder.Record(null!));
        Assert.Throws<InvalidOperationException>(() => recorder.Record(new BrowserLinkMoveOperation("missing", "output")));
        Assert.Throws<InvalidOperationException>(() => recorder.Record(new BrowserLinkTextOperation("input", "(module)")));
        Assert.Throws<InvalidOperationException>(() => recorder.Record(new BrowserLinkMoveOperation("input", "output")));
        Assert.Empty(state.Operations);
        Assert.True(state.Available.SetEquals(["input"]));
        Assert.Throws<ArgumentNullException>(() => new BrowserLinkOperationRecorder(null!, BrowserLinkPlanValidation.CreateInspector()));
        Assert.Throws<ArgumentNullException>(() => new BrowserLinkOperationRecorder(state, null!));
    }

    private static BrowserLinkCaptureState State() => new(["input"], ["output"], ["temporary", "text"]);
    private static IBrowserLinkOperationRecorder Create(BrowserLinkCaptureState state) =>
        Assert.IsAssignableFrom<IBrowserLinkOperationRecorder>(new BrowserLinkOperationRecorder(state, BrowserLinkPlanValidation.CreateInspector()));
}
