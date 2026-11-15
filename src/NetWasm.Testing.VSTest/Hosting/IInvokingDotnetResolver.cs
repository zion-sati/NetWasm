// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using NetWasm.Testing.VSTest.Configuration;

namespace NetWasm.Testing.VSTest.Hosting;

internal interface IInvokingDotnetResolver
{
    string Resolve(NetWasmRunConfiguration configuration);
}
