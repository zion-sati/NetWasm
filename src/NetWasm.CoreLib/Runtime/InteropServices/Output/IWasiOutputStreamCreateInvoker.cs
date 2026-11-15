// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.Output;

internal interface IWasiOutputStreamCreateInvoker
{
    // stream.new: the readable end in the low 32 bits, the writable end above.
    long Create();
}
