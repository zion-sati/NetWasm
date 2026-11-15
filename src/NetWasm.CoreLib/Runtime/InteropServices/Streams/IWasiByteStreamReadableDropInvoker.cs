// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.Streams;

internal interface IWasiByteStreamReadableDropInvoker
{
    void Drop(int readable);
}
