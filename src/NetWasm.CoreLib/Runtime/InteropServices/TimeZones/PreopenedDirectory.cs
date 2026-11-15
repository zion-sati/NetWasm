// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.TimeZones
{
    internal readonly struct PreopenedDirectory
    {
        internal PreopenedDirectory(int handle, string path)
        {
            Handle = handle;
            Path = path ?? throw new ArgumentNullException();
        }

        internal int Handle { get; }
        internal string Path { get; }
    }
}
