// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel.Browser.Plans;

namespace NetWasm.Compiler.ComponentModel.Tests.Browser.Plans;

public sealed class BrowserLinkCaptureFileExistenceTests
{
    [Fact]
    public void ExistenceReflectsAvailableValuesRatherThanTheLastStage()
    {
        var state = new BrowserLinkCaptureState(["input"], ["output"], ["temporary"]);
        var existence = Assert.IsAssignableFrom<IFileExistence>(new BrowserLinkCaptureFileExistence(state));
        Assert.True(existence.Exists("input"));
        Assert.False(existence.Exists("temporary"));
        Assert.False(existence.Exists("INPUT"));
        state.Available.Add("temporary");
        Assert.True(existence.Exists("temporary"));
        state.Available.Remove("temporary");
        Assert.False(existence.Exists("temporary"));
        Assert.Throws<ArgumentNullException>(() => new BrowserLinkCaptureFileExistence(null!));
    }
}
