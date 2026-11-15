// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace NetWasm.Testing.VSTest.Hosting;

internal interface ITestHostProcessTerminator
{
    void Terminate(
        Func<bool> hasExited,
        Action kill,
        Action waitForExit);
}
