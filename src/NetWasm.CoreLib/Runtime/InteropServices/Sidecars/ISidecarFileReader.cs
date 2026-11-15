// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.Sidecars
{
    internal interface ISidecarFileReader
    {
        // The whole file of that name under the sidecar mount; throws when it cannot be read.
        byte[] Read(string fileName);
    }
}
