// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.Sidecars
{
    using System.Runtime.InteropServices.WebAssembly;

    // Writes to the program's own standard error, as Environment.FailFast does, without Console.
    internal sealed class ProgramStandardErrorWriter : IStandardErrorWriter
    {
        public void Write(ReadOnlySpan<byte> bytes) => PlatformServices.StandardStreams.WriteError(bytes);
    }
}
