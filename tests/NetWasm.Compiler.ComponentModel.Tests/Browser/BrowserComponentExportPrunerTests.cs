// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel.Browser;

namespace NetWasm.Compiler.ComponentModel.Tests.Browser;

public sealed class BrowserComponentExportPrunerTests
{
    [Fact]
    public void VirtualFilesEnforceTheirSingleInputAndOutputContract()
    {
        var existence = new BrowserComponentExportPruner.VirtualModuleExistence();
        Assert.True(existence.Exists("merged.wasm"));
        Assert.False(existence.Exists("other.wasm"));
        var source = new byte[] { 1, 2, 3 };
        var reader = new BrowserComponentExportPruner.VirtualModuleReader(source);
        Assert.Same(source, reader.Read("merged.wasm"));
        Assert.Throws<InvalidOperationException>(() => reader.Read("other.wasm"));
        var writer = new BrowserComponentExportPruner.VirtualModuleWriter();
        Assert.Throws<InvalidOperationException>(() => _ = writer.Module);
        Assert.Throws<InvalidOperationException>(() => writer.Write("other.wasm", [4, 5, 6]));
        writer.Write("sanitized.wasm", [4, 5, 6]);
        Assert.Equal([4, 5, 6], writer.Module);
        Assert.Throws<InvalidOperationException>(() => writer.Write("sanitized.wasm", [7]));
    }
}
