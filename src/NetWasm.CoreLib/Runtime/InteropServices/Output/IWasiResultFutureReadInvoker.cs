// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.Output;

internal interface IWasiResultFutureReadInvoker
{
    // An async-lowered future.read into a result<_, error-code> buffer.
    int Read(int readable, nuint result);
}
