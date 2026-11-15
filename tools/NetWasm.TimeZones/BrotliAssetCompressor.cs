// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.IO.Compression;

namespace NetWasm.TimeZones;

internal sealed class BrotliAssetCompressor : IBrotliAssetCompressor
{
    public byte[] Compress(byte[] contents)
    {
        ArgumentNullException.ThrowIfNull(contents);
        using var output = new MemoryStream();
        using (var compressor = new BrotliStream(
            output,
            CompressionLevel.SmallestSize,
            leaveOpen: true))
        {
            compressor.Write(contents);
        }
        return output.ToArray();
    }
}
