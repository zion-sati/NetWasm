// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Frozen;
using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel.Browser.Plans;

namespace NetWasm.Compiler.ComponentModel.Tests.Browser.Plans;

public sealed class BrowserLinkNativeCaptureTests
{
    [Fact]
    public void MergeBuilderPreservesArgumentsAndExtractsOnlyPathOperands()
    {
        var builder = CreateMergeBuilder();
        ImmutableArray<string> arguments = ["first", "application", "second", "runtime", "--output", "out"];
        var result = builder.Build(arguments);
        Assert.Equal("wasm-merge", result.ToolId);
        Assert.Equal(arguments, result.Arguments);
        Assert.Equal(["first", "second"], result.InputPaths);
        Assert.Equal(["out"], result.OutputPaths);
        foreach (var invalid in new ImmutableArray<string>[]
        {
            default, [], ["--output", "out"], ["first", "--output", "out"],
            ["first", "application", "--output"],
            [.. arguments, "--unknown"],
        }) Assert.Throws<ArgumentException>(() => builder.Build(invalid));
        Assert.Throws<ArgumentNullException>(() => new BrowserLinkNativeMergeBuilder(null!));
    }

    [Fact]
    public void OptimizationBuilderPreservesArgumentsAndRejectsUnsupportedGrammar()
    {
        var builder = CreateOptimizationBuilder();
        ImmutableArray<string> arguments = ["input", "-O3", "--output", "out"];
        var result = builder.Build(arguments);
        Assert.Equal("wasm-opt", result.ToolId);
        Assert.Equal(arguments, result.Arguments);
        Assert.Equal(["input"], result.InputPaths);
        Assert.Equal(["out"], result.OutputPaths);
        foreach (var invalid in new ImmutableArray<string>[]
        {
            default, [], ["input", "--output", "out"],
            ["input", "-O3", "--unknown", "out"],
            ["input", "--unknown", "--output", "out"],
        }) Assert.Throws<ArgumentException>(() => builder.Build(invalid));
        Assert.Throws<ArgumentNullException>(() => new BrowserLinkNativeOptimizationBuilder(null!));
    }

    [Fact]
    public void ToolCaptureRecordsValidatedCommandAndDoesNotRecordRejectedCommands()
    {
        var recorder = new Recorder();
        var capture = BrowserLinkPlanValidation.CreateToolCapture(recorder);
        Assert.Throws<ArgumentException>(() => capture.Run(" ", []));
        Assert.Throws<InvalidOperationException>(() => capture.Run("unknown", []));
        Assert.Throws<ArgumentException>(() => capture.Run("wasm-opt", []));
        Assert.Empty(recorder.Operations);
        var result = capture.Run("wasm-opt", ["input", "-O3", "--output", "out"]);
        Assert.Equal(0, result.ExitCode);
        var operation = Assert.IsType<BrowserLinkToolOperation>(Assert.Single(recorder.Operations));
        Assert.Equal("wasm-opt", operation.ToolId);
        Assert.Equal(["input", "-O3", "--output", "out"], operation.Arguments);
        var builders = new Dictionary<string, IBrowserLinkNativeToolBuilder>().ToFrozenDictionary();
        Assert.Throws<ArgumentNullException>(() => new BrowserLinkNativeToolCapture(null!, builders));
        Assert.Throws<ArgumentNullException>(() => new BrowserLinkNativeToolCapture(recorder, null!));
    }

    private static IBrowserLinkNativeToolBuilder CreateMergeBuilder() =>
        Assert.IsAssignableFrom<IBrowserLinkNativeToolBuilder>(new BrowserLinkNativeMergeBuilder(BrowserLinkPlanValidation.CreateInspector()));

    private static IBrowserLinkNativeToolBuilder CreateOptimizationBuilder() =>
        Assert.IsAssignableFrom<IBrowserLinkNativeToolBuilder>(new BrowserLinkNativeOptimizationBuilder(BrowserLinkPlanValidation.CreateInspector()));

    private sealed class Recorder : IBrowserLinkOperationRecorder
    {
        public List<BrowserLinkOperation> Operations { get; } = [];
        public void Record(BrowserLinkOperation operation) => Operations.Add(operation);
    }
}
