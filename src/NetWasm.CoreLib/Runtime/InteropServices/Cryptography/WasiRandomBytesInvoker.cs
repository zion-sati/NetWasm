// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.Cryptography
{
    internal sealed class WasiRandomBytesInvoker : IWasiRandomBytesInvoker
    {
        public void Invoke(long length, nuint result) =>
            WasiRandomBytesImports.GetRandomBytes(length, result);
    }
}
