// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.ConsoleServices;

using System.Runtime.InteropServices.WebAssembly;

internal interface IWasiTerminalDropInvoker
{
    // Drops the terminal resource the stream's get-terminal function returned.
    void Drop(StandardStream stream, int handle);
}
