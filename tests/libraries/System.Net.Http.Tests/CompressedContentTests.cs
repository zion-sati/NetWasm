// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.System.Net.Http.Tests;

// GZipCompressedContent and BrotliCompressedContent compress through the native zlib and Brotli codecs, so they run
// here, in a linked application, rather than in the compiler's corpus. Each body decompresses to the original through
// the matching stream, synchronously and asynchronously.
public sealed class CompressedContentTests
{
    private const string Text = "NetWasm compresses request bodies as .NET does, NetWasm compresses request bodies.";

    [Test]
    public async Task GZipContentDecompressesToTheOriginal()
    {
        using var content = new GZipCompressedContent(new StringContent(Text), CompressionLevel.Optimal);

        await Assert.That(string.Join(",", content.Headers.ContentEncoding)).IsEqualTo("gzip");
        await Assert.That(content.Headers.ContentType!.MediaType).IsEqualTo("text/plain");
        var compressed = await content.ReadAsByteArrayAsync();
        await Assert.That(Decompress(new GZipStream(new MemoryStream(compressed), CompressionMode.Decompress))).IsEqualTo(Text);
    }

    [Test]
    public async Task GZipContentCopiesSynchronously()
    {
        using var content = new GZipCompressedContent(new StringContent(Text), CompressionLevel.Fastest);
        using var synchronous = new MemoryStream();
        content.CopyTo(synchronous, null, CancellationToken.None);

        await Assert.That(Decompress(new GZipStream(new MemoryStream(synchronous.ToArray()), CompressionMode.Decompress)))
            .IsEqualTo(Text);
    }

    [Test]
    public async Task BrotliContentDecompressesToTheOriginal()
    {
        using var content = new BrotliCompressedContent(new StringContent(Text), CompressionLevel.SmallestSize);

        await Assert.That(string.Join(",", content.Headers.ContentEncoding)).IsEqualTo("br");
        var compressed = await content.ReadAsByteArrayAsync();
        await Assert.That(compressed.Length).IsLessThan(Encoding.UTF8.GetByteCount(Text));
        await Assert.That(Decompress(new BrotliStream(new MemoryStream(compressed), CompressionMode.Decompress))).IsEqualTo(Text);

        using var synchronous = new MemoryStream();
        using var again = new BrotliCompressedContent(new StringContent(Text));
        again.CopyTo(synchronous, null, CancellationToken.None);
        await Assert.That(Decompress(new BrotliStream(new MemoryStream(synchronous.ToArray()), CompressionMode.Decompress)))
            .IsEqualTo(Text);
    }

    [Test]
    public async Task CompressedContentRejectsAnUnknownLevel()
    {
        await Assert.That(() => new GZipCompressedContent(new StringContent(Text), (CompressionLevel)42))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new BrotliCompressedContent(null!)).Throws<ArgumentNullException>();
    }

    private static string Decompress(Stream decompressor)
    {
        using var reader = new StreamReader(decompressor, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
