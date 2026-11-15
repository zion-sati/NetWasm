// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.IO;

namespace NetWasm.Compiler.ComponentModel.Binary;

internal static class WasmUnsignedLeb128
{
    public static void Write(Stream output, uint value)
    {
        do
        {
            var next = (byte)(value & 0x7f);
            value >>= 7;
            if (value != 0)
            {
                next |= 0x80;
            }
            output.WriteByte(next);
        }
        while (value != 0);
    }
}
