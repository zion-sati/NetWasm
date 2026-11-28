// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler;
using NetWasm.Compiler.Diagnostics;

namespace NetWasm.Testing.CompilerHost;

internal sealed class ConsoleCompilerProgressReporter : ICompilerProgressReporter
{
    public void Report(CompilerProgressStage stage)
    {
        // A failure is an outcome, not progress toward Complete; the host reports it through its exit.
        if (stage == CompilerProgressStage.Failed)
        {
            return;
        }
        Console.Error.WriteLine(
            $"NETWASM_PROGRESS {(int)stage}/{(int)CompilerProgressStage.Complete}");
    }
}
