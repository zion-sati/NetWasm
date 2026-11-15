// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel.Browser.Plans;

namespace NetWasm.Compiler.ComponentModel.Tests.Browser.Plans;

public sealed class BrowserLinkToolInspectorTests
{
    [Fact]
    public void RegistrationRejectsMissingNamesImplementationsAndDuplicates()
    {
        Assert.Throws<ArgumentNullException>(() => new BrowserLinkToolInspector(null!));
        Assert.Throws<ArgumentException>(() => new BrowserLinkToolInspector([new(" ", new BrowserLinkOptimizationInspector())]));
        Assert.Throws<ArgumentNullException>(() => new BrowserLinkToolInspector([new("wasm-opt", null!)]));
        Assert.Throws<ArgumentException>(() => new BrowserLinkToolInspector([
            new("wasm-opt", new BrowserLinkOptimizationInspector()), new("wasm-opt", new BrowserLinkOptimizationInspector()),
        ]));
    }

    [Fact]
    public void DispatchRequiresAnExplicitRegisteredToolIdentity()
    {
        var inspector = Assert.IsAssignableFrom<IBrowserLinkOperationInspector>(new BrowserLinkToolInspector([
            new("wasm-opt", new BrowserLinkOptimizationInspector()),
        ]));
        var operation = new BrowserLinkToolOperation("wasm-opt", ["in", "-Oz", "--output", "out"], ["in"], ["out"]);
        Assert.Equal("wasm-opt", inspector.Inspect(operation).ToolId);
        Assert.Throws<ArgumentException>(() => inspector.Inspect(null!));
        Assert.Throws<ArgumentException>(() => inspector.Inspect(new BrowserLinkTextOperation("out", "text")));
        Assert.Throws<ArgumentException>(() => inspector.Inspect(operation with { ToolId = " " }));
        Assert.Throws<InvalidOperationException>(() => inspector.Inspect(operation with { ToolId = "WASM-OPT" }));
    }
}
