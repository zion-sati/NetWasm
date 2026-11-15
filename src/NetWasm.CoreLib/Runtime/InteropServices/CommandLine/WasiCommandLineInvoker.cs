// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.CommandLine
{
    internal sealed class WasiCommandLineInvoker : IWasiCommandLineInvoker
    {
        public void Invoke(nuint result) =>
            WasiCommandLineImports.GetArguments(result);
    }
}
