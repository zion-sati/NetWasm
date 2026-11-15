// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class WasmEmissionRequestTests
{
    [Fact]
    public void CommandOnlyRequestsHaveNoReenterableHostEntries()
    {
        var request = CreateEmissionRequest(new FakeProgram());

        Assert.Empty(request.RequestedExports);
        Assert.True(request.HostCallbacks.IsEmpty);
        Assert.False(request.HasReenterableHostEntries);
    }

    [Fact]
    public void RequestedExportsAreReenterableHostEntries()
    {
        var request = CreateEmissionRequest(new FakeProgram()) with
        {
            RequestedExports = new Dictionary<string, EntityKey> { ["run"] = EntryKey },
        };

        Assert.True(request.HasReenterableHostEntries);
    }

    [Fact]
    public void HostCallbacksAreReenterableHostEntries()
    {
        var program = new FakeProgram();
        var method = program.GetMethod(EntryKey);
        var invoke = new MethodInstanceModel(
            method,
            CliTypeIdentity.Named(Assembly, "Test", "Callback", isValueType: false),
            [],
            method.Signature);
        var request = CreateEmissionRequest(program) with
        {
            HostCallbacks = [new HostCallbackDeclaration(EntryKey, 0, invoke, "callback")],
        };

        Assert.Empty(request.RequestedExports);
        Assert.True(request.HasReenterableHostEntries);
    }
}
