// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.IO;
using System.Threading.Tasks;

namespace NetWasm.Wit.Netwasm.Test.Stream._1._0._0;

public static partial class TestStreamExports
{
    // Writes four bytes through stdout's stream and returns 42 once the host reports the write.
    public static async partial Task<uint> Write()
    {
        var written = await StdoutImports.WriteViaStream(new MemoryStream([1, 2, 3, 4]));
        return written.IsOk ? 42u : 0u;
    }

    // The function entry nwc compiles a library from.
    public static int Run(int value) => value;
}
