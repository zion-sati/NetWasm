// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

// SixLabors.ImageSharp: pixel access, its PNG, BMP, GIF, JPEG, TIFF and WebP encoders and decoders, and resampling,
// rotation and color processing, compared by their bytes' hashes.
using System;
using System.IO;
using System.Security.Cryptography;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using var image = new Image<Rgba32>(97, 61);
image.ProcessPixelRows(accessor =>
{
    for (var y = 0; y < accessor.Height; y++)
    {
        var row = accessor.GetRowSpan(y);
        for (var x = 0; x < row.Length; x++)
        {
            row[x] = new Rgba32((byte)(x * 3 + y), (byte)(x ^ y), (byte)(y * 5), (byte)(255 - x));
        }
    }
});
Console.WriteLine("source " + Pixels(image));
Console.WriteLine("png " + Encoded(image, new PngEncoder()));
Console.WriteLine("bmp " + Encoded(image, new BmpEncoder()));
Console.WriteLine("gif " + Encoded(image, new GifEncoder()));
Console.WriteLine("jpeg " + Encoded(image, new JpegEncoder { Quality = 85 }));
Console.WriteLine("tiff " + Encoded(image, new TiffEncoder()));
Console.WriteLine("webp lossless " + Encoded(image, new WebpEncoder { FileFormat = WebpFileFormatType.Lossless }));
Console.WriteLine("webp lossy " + Encoded(image, new WebpEncoder { FileFormat = WebpFileFormatType.Lossy, Quality = 80 }));

using (var copy = image.Clone(context => context.Rotate(RotateMode.Rotate90).Flip(FlipMode.Horizontal).Crop(new Rectangle(5, 7, 40, 30))))
{
    Console.WriteLine($"rotated {copy.Width}x{copy.Height} {Pixels(copy)}");
}
using (var copy = image.Clone(context => context.Resize(48, 30, KnownResamplers.NearestNeighbor)))
{
    Console.WriteLine($"nearest {Pixels(copy)}");
}
using (var copy = image.Clone(context => context.Resize(48, 30)))
{
    Console.WriteLine($"bicubic {Pixels(copy)}");
}
using (var copy = image.Clone(context => context.Grayscale().Invert()))
{
    Console.WriteLine($"grayscale {Pixels(copy)}");
}

var stream = new MemoryStream();
image.SaveAsPng(stream);
using (var decoded = Image.Load<Rgba32>(stream.ToArray()))
{
    Console.WriteLine($"png round trip {decoded.Width}x{decoded.Height} {Pixels(decoded)}");
}
stream = new MemoryStream();
image.SaveAsJpeg(stream, new JpegEncoder { Quality = 90 });
using (var decoded = Image.Load<Rgba32>(stream.ToArray()))
{
    Console.WriteLine($"jpeg round trip {decoded.Width}x{decoded.Height} {Pixels(decoded)}");
}
foreach (var encoder in new SixLabors.ImageSharp.Formats.IImageEncoder[] { new TiffEncoder(), new WebpEncoder { FileFormat = WebpFileFormatType.Lossless } })
{
    stream = new MemoryStream();
    image.Save(stream, encoder);
    using var decoded = Image.Load<Rgba32>(stream.ToArray());
    Console.WriteLine($"{encoder.GetType().Name} round trip {decoded.Width}x{decoded.Height} {Pixels(decoded)}");
}
var info = Image.Identify(stream.ToArray());
Console.WriteLine($"{info.Width}x{info.Height} {info.Metadata.DecodedImageFormat?.Name}");

static string Pixels(Image<Rgba32> image)
{
    var bytes = new byte[image.Width * image.Height * 4];
    image.CopyPixelDataTo(bytes);
    return Convert.ToHexString(SHA256.HashData(bytes));
}

static string Encoded(Image image, SixLabors.ImageSharp.Formats.IImageEncoder encoder)
{
    var stream = new MemoryStream();
    image.Save(stream, encoder);
    return stream.Length + " " + Convert.ToHexString(SHA256.HashData(stream.ToArray()));
}
