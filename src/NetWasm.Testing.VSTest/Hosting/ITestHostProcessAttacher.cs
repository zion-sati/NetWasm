// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace NetWasm.Testing.VSTest.Hosting;

internal interface ITestHostProcessAttacher
{
    ITestHostProcess Attach(int processId, Action<int, int> exited);
}
