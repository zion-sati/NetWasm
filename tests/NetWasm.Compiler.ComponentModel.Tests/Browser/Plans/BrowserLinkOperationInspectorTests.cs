// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel.Browser.Plans;

namespace NetWasm.Compiler.ComponentModel.Tests.Browser.Plans;

public sealed class BrowserLinkOperationInspectorTests
{
    [Fact]
    public void RegistrationRejectsNullAndDuplicateCapabilities()
    {
        Assert.Throws<ArgumentNullException>(() => new BrowserLinkOperationInspector(null!));
        Assert.Throws<ArgumentNullException>(() => new BrowserLinkOperationInspector([new(null!, new BrowserLinkTextInspector())]));
        Assert.Throws<ArgumentNullException>(() => new BrowserLinkOperationInspector([new(typeof(BrowserLinkTextOperation), null!)]));
        Assert.Throws<ArgumentException>(() => new BrowserLinkOperationInspector([
            new(typeof(BrowserLinkTextOperation), new BrowserLinkTextInspector()),
            new(typeof(BrowserLinkTextOperation), new BrowserLinkTextInspector()),
        ]));
    }

    [Fact]
    public void DispatchUsesExactOperationTypeAndRejectsMissingRegistration()
    {
        var inspector = Assert.IsAssignableFrom<IBrowserLinkOperationInspector>(new BrowserLinkOperationInspector([
            new(typeof(BrowserLinkTextOperation), new BrowserLinkTextInspector()),
        ]));
        Assert.Equal("text", inspector.Inspect(new BrowserLinkTextOperation("out", "(module)")).Capability);
        Assert.Throws<ArgumentNullException>(() => inspector.Inspect(null!));
        Assert.Throws<InvalidOperationException>(() => inspector.Inspect(new BrowserLinkMoveOperation("in", "out")));
    }
}
