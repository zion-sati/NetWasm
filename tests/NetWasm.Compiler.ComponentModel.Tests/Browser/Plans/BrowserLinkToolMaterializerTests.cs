// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel.Browser.Plans;

namespace NetWasm.Compiler.ComponentModel.Tests.Browser.Plans;

public sealed class BrowserLinkToolMaterializerTests
{
    [Fact]
    public void MapsCollidingBasenamesWithoutRewritingModuleLabels()
    {
        var operation = new BrowserLinkToolOperation("wasm-merge",
            ["/a/input.wasm", "/a/input.wasm", "/b/input.wasm", "runtime", "--output", "/out/result.wasm"],
            ["/a/input.wasm", "/b/input.wasm"], ["/out/result.wasm"]);
        var result = Create(new BrowserLinkMergeInspector()).Materialize(operation);

        Assert.Equal("wasm-merge", result.ToolId);
        Assert.Equal(["input-0", "/a/input.wasm", "input-1", "runtime", "--output", "output-0"], result.Arguments);
        Assert.Equal([new("/a/input.wasm", "input-0"), new BrowserLinkArtifactBinding("/b/input.wasm", "input-1")], result.Inputs);
        Assert.Equal([new BrowserLinkArtifactBinding("/out/result.wasm", "output-0")], result.Outputs);
        Assert.Equal("/a/input.wasm", operation.Arguments[0]);
    }

    [Fact]
    public void SeparatesAnInPlaceOutputWithoutChangingOptimizationPolicy()
    {
        var operation = new BrowserLinkToolOperation("wasm-opt",
            ["/stage.wasm", "-Oz", "--converge", "--output", "/stage.wasm"], ["/stage.wasm"], ["/stage.wasm"]);
        var result = Create(new BrowserLinkOptimizationInspector()).Materialize(operation);

        Assert.Equal(["input-0", "-Oz", "--converge", "--output", "output-0"], result.Arguments);
        Assert.Equal(result.Inputs[0].LogicalPath, result.Outputs[0].LogicalPath);
        Assert.NotEqual(result.Inputs[0].PhysicalName, result.Outputs[0].PhysicalName);
        Assert.Equal("/stage.wasm", operation.Arguments[^1]);
    }

    [Fact]
    public void RejectsMissingDependenciesAndInvalidOperations()
    {
        Assert.Throws<ArgumentNullException>(() => new BrowserLinkToolMaterializer(null!));
        Assert.Throws<ArgumentNullException>(() => Create(new BrowserLinkOptimizationInspector()).Materialize(null!));
        Assert.Throws<ArgumentException>(() => Create(new BrowserLinkOptimizationInspector()).Materialize(
            new BrowserLinkToolOperation("unknown", [], [], [])));
    }

    private static IBrowserLinkToolMaterializer Create(IBrowserLinkOperationInspector inspector) =>
        Assert.IsAssignableFrom<IBrowserLinkToolMaterializer>(new BrowserLinkToolMaterializer(inspector));

    [Fact]
    public void RejectsUnboundInputAndOutputPositionsBeforeMaterializing()
    {
        var operation = new BrowserLinkToolOperation("wasm-opt", ["in", "-Oz", "--output", "out"], ["in"], ["out"]);
        var complete = new BrowserLinkOperationContract("tool", ["in"], ["out"], [])
        {
            ToolId = "wasm-opt",
            Arguments = operation.Arguments,
            InputArgumentIndices = [0],
            OutputArgumentIndices = [3],
        };
        foreach (var invalid in new[]
        {
            complete with { ToolId = null },
            complete with { InputArgumentIndices = [] },
            complete with { OutputArgumentIndices = [] },
        }) Assert.Throws<InvalidOperationException>(() => Create(new FixedInspector(invalid)).Materialize(operation));
        Assert.Equal("in", operation.Arguments[0]);
    }

    private sealed class FixedInspector(BrowserLinkOperationContract contract) : IBrowserLinkOperationInspector
    {
        public BrowserLinkOperationContract Inspect(BrowserLinkOperation operation) => contract;
    }
}
