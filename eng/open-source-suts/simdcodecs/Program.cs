// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

// Sdcb.SimdCodecs' managed SIMD codecs: PNG, JPEG (baseline and progressive), WebP (lossy and lossless), QOI and
// BMP, each encoded, identified and decoded at full and half scale, compared by hashes.
using System;
using System.IO;
using System.Security.Cryptography;
using Sdcb.SimdCodecs;
using Sdcb.SimdCodecs.Abstractions;
using Sdcb.SimdCodecs.Jpeg;
using Sdcb.SimdCodecs.Webp;

const int width = 257;
const int height = 131;
var pixels = new byte[width * height * 4];
for (var y = 0; y < height; y++)
{
    for (var x = 0; x < width; x++)
    {
        var offset = (y * width + x) * 4;
        pixels[offset] = (byte)(x * 255 / width);
        pixels[offset + 1] = (byte)(y * 255 / height);
        pixels[offset + 2] = (byte)((x * 7 + y * 13) & 0xFF);
        pixels[offset + 3] = (byte)(x % 17 == 0 ? 128 : 255);
    }
}

Run("png", ImageFormat.Png, null);
Run("jpeg q85", ImageFormat.Jpeg, new JpegEncodeOptions { Quality = 85 });
Run("jpeg progressive", ImageFormat.Jpeg, new JpegEncodeOptions { Quality = 75, Progressive = true });
Run("webp lossy", ImageFormat.Webp, new WebpLossyEncodeOptions { Quality = 75 });
Run("webp lossless", ImageFormat.Webp, new WebpLosslessEncodeOptions());
Run("qoi", ImageFormat.Qoi, null);
Run("bmp", ImageFormat.Bmp, null);

void Run(string name, ImageFormat format, EncodeOptions? options)
{
    var output = new MemoryStream();
    ImageCodecs.Encode(pixels, width, height, PixelFormat.Rgba32, format, output, options: options);
    var encoded = output.ToArray();
    Console.WriteLine($"{name} encoded {encoded.Length} {Convert.ToHexString(SHA256.HashData(encoded))}");
    Console.WriteLine($"{name} format {ImageCodecs.DetectFormat(encoded)} info {ImageCodecs.TryReadInfo(encoded, out var info)} {info.Width}x{info.Height}");
    using var decoded = ImageCodecs.Decode(encoded, PixelFormat.Rgba32);
    Console.WriteLine($"{name} decoded {decoded.Width}x{decoded.Height} {Convert.ToHexString(SHA256.HashData(decoded.Pixels))}");
    using var small = ImageCodecs.Decode(encoded, PixelFormat.Bgra32, new DecodeOptions { ScaleDenominator = 2 });
    Console.WriteLine($"{name} half {small.Width}x{small.Height} {Convert.ToHexString(SHA256.HashData(small.Pixels))}");
}
