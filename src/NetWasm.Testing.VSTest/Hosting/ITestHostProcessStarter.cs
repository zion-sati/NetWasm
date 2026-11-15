// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using Microsoft.VisualStudio.TestPlatform.ObjectModel;

namespace NetWasm.Testing.VSTest.Hosting;

internal interface ITestHostProcessStarter
{
    ITestHostProcess Start(
        TestProcessStartInfo startInfo,
        Action<string> standardOutput,
        Action<string> standardError,
        Action<int, int> exited);
}
