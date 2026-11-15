// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel.Inlining;

namespace NetWasm.Compiler.ComponentModel.Tests.Inlining;

public sealed class WasmCoreModuleInlineAnnotationEditorTests
{
    private static readonly string[] ExpectedStages = ["read", "filter", "write"];

    [Theory]
    [InlineData(WasmInlineAnnotationSelection.Preserve, false)]
    [InlineData(WasmInlineAnnotationSelection.Preserve, true)]
    [InlineData(WasmInlineAnnotationSelection.SizeAware, false)]
    [InlineData(WasmInlineAnnotationSelection.SizeAware, true)]
    [InlineData(WasmInlineAnnotationSelection.Remove, false)]
    [InlineData(WasmInlineAnnotationSelection.Remove, true)]
    public void ReadsAndFiltersBeforeWritingIncludingInPlaceCleanup(
        WasmInlineAnnotationSelection selection, bool inPlace)
    {
        var stages = new RecordingStages();
        var output = inPlace ? "input" : "output";

        Create(stages).Rewrite("input", output, selection);

        Assert.Equal(["read", "filter", "write"], stages.Calls);
        Assert.Equal("input", stages.ReadPath);
        Assert.Same(stages.Input, stages.FilterInput);
        Assert.Equal(selection, stages.Selection);
        Assert.Equal(output, stages.WritePath);
        Assert.Same(stages.Filtered, stages.Written);
    }

    [Fact]
    public void RejectsMissingCapabilitiesWithoutAccessingFiles()
    {
        var stages = new RecordingStages();
        Assert.Throws<ArgumentNullException>(() => new WasmCoreModuleInlineAnnotationEditor(null!, stages, stages));
        Assert.Throws<ArgumentNullException>(() => new WasmCoreModuleInlineAnnotationEditor(stages, null!, stages));
        Assert.Throws<ArgumentNullException>(() => new WasmCoreModuleInlineAnnotationEditor(stages, stages, null!));
        Assert.Empty(stages.Calls);
    }

    [Theory]
    [InlineData(null, "output")]
    [InlineData("", "output")]
    [InlineData(" ", "output")]
    [InlineData("input", null)]
    [InlineData("input", "")]
    [InlineData("input", " ")]
    public void RejectsInvalidPathsBeforeReading(string? input, string? output)
    {
        var stages = new RecordingStages();
        Assert.ThrowsAny<ArgumentException>(() => Create(stages).Rewrite(
            input!, output!, WasmInlineAnnotationSelection.Preserve));
        Assert.Empty(stages.Calls);
    }

    [Fact]
    public void RejectsInvalidSelectionBeforeReading()
    {
        var stages = new RecordingStages();
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(stages).Rewrite(
            "input", "output", (WasmInlineAnnotationSelection)255));
        Assert.Empty(stages.Calls);
    }

    [Theory]
    [InlineData("read", 1)]
    [InlineData("filter", 2)]
    [InlineData("write", 3)]
    public void PropagatesOriginalFailureWithoutRunningLaterStages(string failureStage, int calls)
    {
        var stages = new RecordingStages { FailureStage = failureStage };

        var failure = Assert.Throws<InvalidOperationException>(() => Create(stages).Rewrite(
            "input", "output", WasmInlineAnnotationSelection.Remove));

        Assert.Same(stages.Failure, failure);
        Assert.Equal(ExpectedStages.Take(calls), stages.Calls);
        Assert.Null(stages.Written);
    }

    private static IWasmCoreModuleInlineAnnotationEditor Create(RecordingStages stages) =>
        Assert.IsAssignableFrom<IWasmCoreModuleInlineAnnotationEditor>(
            new WasmCoreModuleInlineAnnotationEditor(stages, stages, stages));

    private sealed class RecordingStages : IByteFileReader, IByteFileWriter, IWasmInlineAnnotationFilter
    {
        public List<string> Calls { get; } = [];
        public byte[] Input { get; } = [1, 2];
        public byte[] Filtered { get; } = [3];
        public InvalidOperationException Failure { get; } = new("stage failure");
        public string? FailureStage { get; init; }
        public string? ReadPath { get; private set; }
        public byte[]? FilterInput { get; private set; }
        public WasmInlineAnnotationSelection? Selection { get; private set; }
        public string? WritePath { get; private set; }
        public byte[]? Written { get; private set; }

        public byte[] Read(string path)
        {
            Record("read");
            ReadPath = path;
            return Input;
        }

        public byte[] Filter(byte[] source, WasmInlineAnnotationSelection selection)
        {
            Record("filter");
            FilterInput = source;
            Selection = selection;
            return Filtered;
        }

        public void Write(string path, byte[] content)
        {
            Record("write");
            WritePath = path;
            Written = content;
        }

        private void Record(string stage)
        {
            Calls.Add(stage);
            if (stage == FailureStage)
                throw Failure;
        }
    }
}
