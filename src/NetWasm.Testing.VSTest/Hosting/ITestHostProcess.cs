// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace NetWasm.Testing.VSTest.Hosting;

internal interface ITestHostProcess : IDisposable
{
    int Id { get; }

    void Terminate();
}
