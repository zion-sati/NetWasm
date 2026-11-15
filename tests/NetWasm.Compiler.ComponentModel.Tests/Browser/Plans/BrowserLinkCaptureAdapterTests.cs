// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel.Browser;
using NetWasm.Compiler.ComponentModel.Browser.Plans;
using NetWasm.Compiler.ComponentModel.Inlining;

namespace NetWasm.Compiler.ComponentModel.Tests.Browser.Plans;

public sealed class BrowserLinkCaptureAdapterTests
{
    [Fact]
    public void TextInlineAndMovePreserveProducerOperands()
    {
        var recorder = new Recorder();
        new BrowserLinkTextCapture(recorder).Write("(module)", "text");
        new BrowserLinkInlineCapture(recorder).Rewrite("input", "inline", WasmInlineAnnotationSelection.Preserve);
        new BrowserLinkMoveCapture(recorder).Move("inline", "output");

        Assert.Equal(new BrowserLinkTextOperation("text", "(module)"), recorder.Operations[0]);
        Assert.Equal(new BrowserLinkInlineOperation("input", "inline", WasmInlineAnnotationSelection.Preserve), recorder.Operations[1]);
        Assert.Equal(new BrowserLinkMoveOperation("inline", "output"), recorder.Operations[2]);
    }

    [Fact]
    public void ValidationPreservesCommandAndRejectsMalformedInputBeforeRecording()
    {
        var recorder = new Recorder();
        var capture = new BrowserLinkValidationCapture(recorder);
        Assert.Throws<ArgumentNullException>(() => capture.Run(null!, "validate"));
        Assert.Throws<ArgumentException>(() => capture.Run([], " "));
        Assert.Throws<ArgumentException>(() => capture.Run(["validate", "input"], "validate"));
        Assert.Empty(recorder.Operations);

        string[] arguments = ["validate", "input", "--features", "all"];
        capture.Run(arguments, "validate");
        var operation = Assert.IsType<BrowserLinkValidationOperation>(Assert.Single(recorder.Operations));
        Assert.Equal("input", operation.Path);
        Assert.Equal(arguments, operation.Arguments);
    }

    [Fact]
    public void CleanupAcceptsOnlyExactlyOwnedPaths()
    {
        var files = new BrowserLinkFileCleanupCapture(ImmutableHashSet.Create(StringComparer.Ordinal, "owned"));
        files.Delete("owned");
        Assert.Throws<InvalidOperationException>(() => files.Delete("Owned"));
        Assert.Throws<InvalidOperationException>(() => files.Delete("other"));
        var directories = new BrowserLinkDirectoryCleanupCapture("work");
        directories.Delete("work");
        Assert.Throws<InvalidOperationException>(() => directories.Delete("work/child"));
        Assert.Throws<InvalidOperationException>(() => directories.Delete("Work"));
    }

    [Fact]
    public void ExportCapturePreservesSelectionAndRejectsMissingSelection()
    {
        var recorder = new Recorder();
        var capture = new BrowserLinkExportCapture(recorder);
        Assert.Throws<ArgumentNullException>(() => capture.Rewrite("input", "output", null!));
        Assert.Empty(recorder.Operations);
        var selection = new WasmExportSelection("component", []);
        capture.Rewrite("input", "output", selection);
        var operation = Assert.IsType<BrowserLinkExportOperation>(Assert.Single(recorder.Operations));
        Assert.Equal("input", operation.InputPath);
        Assert.Equal("output", operation.OutputPath);
        Assert.Equal(selection.ComponentPrefix, operation.ComponentPrefix);
        Assert.Equal(selection.RemovedExports, operation.RemovedExports);
    }

    [Fact]
    public void AdaptersRejectMissingDependencies()
    {
        Assert.Throws<ArgumentNullException>(() => new BrowserLinkTextCapture(null!));
        Assert.Throws<ArgumentNullException>(() => new BrowserLinkInlineCapture(null!));
        Assert.Throws<ArgumentNullException>(() => new BrowserLinkMoveCapture(null!));
        Assert.Throws<ArgumentNullException>(() => new BrowserLinkExportCapture(null!));
        Assert.Throws<ArgumentNullException>(() => new BrowserLinkValidationCapture(null!));
        Assert.Throws<ArgumentNullException>(() => new BrowserLinkFileCleanupCapture(null!));
        Assert.Throws<ArgumentNullException>(() => new BrowserLinkDirectoryCleanupCapture(null!));
    }

    private sealed class Recorder : IBrowserLinkOperationRecorder
    {
        public List<BrowserLinkOperation> Operations { get; } = [];
        public void Record(BrowserLinkOperation operation) => Operations.Add(operation);
    }
}
