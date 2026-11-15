// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.ConsoleServices;

using System.Runtime.InteropServices.WebAssembly;

internal interface IWasiTerminalInvoker
{
    // Calls the stream's get-terminal function: the terminal resource's handle, or 0 when the
    // stream is not a terminal.
    int GetTerminal(StandardStream stream);
}
