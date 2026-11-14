using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace NetWasm.System.IO.Compression.Tests;

// The observations the suite compares with desktop .NET 11. Each compressed output is recorded as its length and
// FNV-1a hash, so a matching fingerprint means the same bytes as desktop's zlib-ng produced.
internal static class CompressionCases
{
    internal static readonly byte[] Corpus = BuildCorpus();

    internal static readonly string[] Formats = ["deflate", "zlib", "gzip"];

    internal static readonly DateTimeOffset EntryTime = new(2024, 5, 6, 7, 8, 10, TimeSpan.Zero);

    // Text runs (compressible) mixed with pseudo-random runs (incompressible), 256 KiB: larger than the window and
    // than the streams' buffers.
    private static byte[] BuildCorpus()
    {
        var data = new byte[256 * 1024];
        var text = Encoding.ASCII.GetBytes("The quick brown fox jumps over the lazy dog. NetWasm compresses with zlib-ng. ");
        uint state = 12345;
        int i = 0;
        while (i < data.Length)
        {
            state = state * 1103515245 + 12345;
            int run = (int)((state >> 16) % 300) + 1;
            if (((state >> 8) & 3) == 0)
            {
                for (int k = 0; k < run && i < data.Length; k++)
                {
                    state = state * 1103515245 + 12345;
                    data[i++] = (byte)(state >> 24);
                }
            }
            else
            {
                int offset = (int)((state >> 4) % (uint)text.Length);
                for (int k = 0; k < run && i < data.Length; k++)
                {
                    data[i++] = text[(offset + k) % text.Length];
                }
            }
        }
        return data;
    }

    internal static string Fingerprint(ReadOnlySpan<byte> data)
    {
        ulong hash = 14695981039346656037;
        foreach (byte value in data)
        {
            hash ^= value;
            hash *= 1099511628211;
        }
        return data.Length + ":" + hash.ToString("X16");
    }

    internal static Stream Compressor(string format, Stream output, CompressionLevel level) => format switch
    {
        "deflate" => new DeflateStream(output, level, leaveOpen: true),
        "zlib" => new ZLibStream(output, level, leaveOpen: true),
        _ => new GZipStream(output, level, leaveOpen: true),
    };

    internal static Stream Compressor(string format, Stream output, ZLibCompressionOptions options) => format switch
    {
        "deflate" => new DeflateStream(output, options, leaveOpen: true),
        "zlib" => new ZLibStream(output, options, leaveOpen: true),
        _ => new GZipStream(output, options, leaveOpen: true),
    };

    internal static Stream Decompressor(string format, Stream input) => format switch
    {
        "deflate" => new DeflateStream(input, CompressionMode.Decompress, leaveOpen: true),
        "zlib" => new ZLibStream(input, CompressionMode.Decompress, leaveOpen: true),
        _ => new GZipStream(input, CompressionMode.Decompress, leaveOpen: true),
    };

    // Writes in prime-sized chunks, so writes straddle the streams' internal buffers.
    internal static void WriteChunked(Stream stream, byte[] input)
    {
        for (int i = 0; i < input.Length; i += 7919)
        {
            stream.Write(input, i, Math.Min(7919, input.Length - i));
        }
    }

    internal static byte[] Compress(string format, CompressionLevel level, byte[] input)
    {
        var output = new MemoryStream();
        using (var stream = Compressor(format, output, level))
        {
            WriteChunked(stream, input);
        }
        return output.ToArray();
    }

    internal static byte[] Compress(string format, ZLibCompressionOptions options, byte[] input)
    {
        var output = new MemoryStream();
        using (var stream = Compressor(format, output, options))
        {
            WriteChunked(stream, input);
        }
        return output.ToArray();
    }

    internal static byte[] Decompress(string format, byte[] data)
    {
        using var input = new MemoryStream(data);
        using var stream = Decompressor(format, input);
        var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }

    // Every level and format, through the streams.
    internal static IEnumerable<string> Levels()
    {
        foreach (string format in Formats)
        {
            foreach (CompressionLevel level in new[] { CompressionLevel.Optimal, CompressionLevel.Fastest, CompressionLevel.NoCompression, CompressionLevel.SmallestSize })
            {
                yield return format + " " + level + " " + Fingerprint(Compress(format, level, Corpus));
            }
        }
    }

    // zlib's own levels 0 to 9 and its strategies, through ZLibCompressionOptions.
    internal static IEnumerable<string> Options()
    {
        for (int level = 0; level <= 9; level++)
        {
            yield return "level " + level + " " + Fingerprint(Compress("zlib", new ZLibCompressionOptions { CompressionLevel = level }, Corpus));
        }
        foreach (ZLibCompressionStrategy strategy in new[] { ZLibCompressionStrategy.Filtered, ZLibCompressionStrategy.HuffmanOnly, ZLibCompressionStrategy.RunLengthEncoding, ZLibCompressionStrategy.Fixed })
        {
            var options = new ZLibCompressionOptions { CompressionLevel = 6, CompressionStrategy = strategy };
            yield return "strategy " + strategy + " " + Fingerprint(Compress("deflate", options, Corpus));
        }
    }

    // Flush emits a sync-flush block (an empty stored block, 00 00 FF FF) and keeps the stream open.
    internal static IEnumerable<string> SyncFlush()
    {
        var output = new MemoryStream();
        using (var stream = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            stream.Write(Encoding.ASCII.GetBytes("hello hello hello"));
            stream.Flush();
            byte[] flushed = output.ToArray();
            yield return "flushed " + Fingerprint(flushed) + " " + Convert.ToHexString(flushed.AsSpan(flushed.Length - 4));
            stream.Write(Encoding.ASCII.GetBytes(" world"));
        }
        byte[] whole = output.ToArray();
        yield return "whole " + Fingerprint(whole) + " " + Encoding.ASCII.GetString(Decompress("deflate", whole));
    }

    // Two gzip members decompress as one stream; data after a deflate stream stays in the base stream.
    internal static IEnumerable<string> Framing()
    {
        var members = new MemoryStream();
        members.Write(Compress("gzip", CompressionLevel.Optimal, Encoding.ASCII.GetBytes("first member, ")));
        members.Write(Compress("gzip", CompressionLevel.Fastest, Encoding.ASCII.GetBytes("second member")));
        yield return "members " + Encoding.ASCII.GetString(Decompress("gzip", members.ToArray()));

        byte[] payload = Compress("deflate", CompressionLevel.Optimal, Encoding.ASCII.GetBytes("payload payload payload"));
        var framed = new MemoryStream();
        framed.Write(payload);
        framed.Write(Encoding.ASCII.GetBytes("TRAILER"));
        framed.Position = 0;
        var read = new MemoryStream();
        using (var stream = new DeflateStream(framed, CompressionMode.Decompress, leaveOpen: true))
        {
            stream.CopyTo(read);
        }
        var rest = new MemoryStream();
        framed.CopyTo(rest);
        yield return "trailing " + Encoding.ASCII.GetString(read.ToArray()) + " payload=" + payload.Length + " rest=" + Encoding.ASCII.GetString(rest.ToArray());
    }

    // The one-shot encoders and decoders added in .NET 11.
    internal static IEnumerable<string> Encoders()
    {
        byte[] input = Corpus.AsSpan(0, 50_000).ToArray();
        long bound = DeflateEncoder.GetMaxCompressedLength(input.Length);
        var destination = new byte[bound];
        bool done = DeflateEncoder.TryCompress(input, destination, out int written, 9);
        yield return "deflate one-shot " + done + " " + Fingerprint(destination.AsSpan(0, written));
        var restored = new byte[input.Length];
        bool inflated = DeflateDecoder.TryDecompress(destination.AsSpan(0, written), restored, out int restoredLength);
        yield return "deflate restore " + inflated + " " + (restoredLength == input.Length && restored.AsSpan().SequenceEqual(input));

        using var encoder = new GZipEncoder(1);
        var buffer = new byte[GZipEncoder.GetMaxCompressedLength(input.Length)];
        OperationStatus status = encoder.Compress(input, buffer, out int consumed, out int produced, isFinalBlock: true);
        yield return "gzip encoder " + status + " " + consumed + " " + Fingerprint(buffer.AsSpan(0, produced));
        using var decoder = new GZipDecoder();
        var back = new byte[input.Length];
        OperationStatus decoded = decoder.Decompress(buffer.AsSpan(0, produced), back, out int read, out int wrote);
        yield return "gzip decoder " + decoded + " " + read + " " + wrote + " " + back.AsSpan().SequenceEqual(input);

        byte[] zlib = new byte[ZLibEncoder.GetMaxCompressedLength(input.Length)];
        bool zlibDone = ZLibEncoder.TryCompress(input, zlib, out int zlibWritten);
        yield return "zlib one-shot " + zlibDone + " " + Fingerprint(zlib.AsSpan(0, zlibWritten));
    }

    // Brotli through BrotliStream at every level, the one-shot encoder at every quality and two window sizes, and
    // the streaming encoder and decoder.
    internal static IEnumerable<string> Brotli()
    {
        foreach (CompressionLevel level in new[] { CompressionLevel.Optimal, CompressionLevel.Fastest, CompressionLevel.NoCompression, CompressionLevel.SmallestSize })
        {
            var output = new MemoryStream();
            using (var stream = new BrotliStream(output, level, leaveOpen: true))
            {
                WriteChunked(stream, Corpus);
            }
            byte[] compressed = output.ToArray();
            var restored = new MemoryStream();
            using (var stream = new BrotliStream(new MemoryStream(compressed), CompressionMode.Decompress))
            {
                stream.CopyTo(restored);
            }
            yield return "stream " + level + " " + Fingerprint(compressed) + " " + restored.ToArray().AsSpan().SequenceEqual(Corpus);
        }

        byte[] input = Corpus.AsSpan(0, 64_000).ToArray();
        var destination = new byte[BrotliEncoder.GetMaxCompressedLength(input.Length)];
        for (int quality = 0; quality <= 11; quality++)
        {
            foreach (int window in new[] { 10, 22 })
            {
                bool done = BrotliEncoder.TryCompress(input, destination, out int written, quality, window);
                yield return "quality " + quality + " window " + window + " " + done + " " + Fingerprint(destination.AsSpan(0, written));
            }
        }

        bool compressedOnce = BrotliEncoder.TryCompress(input, destination, out int length);
        var back = new byte[input.Length];
        bool decoded = BrotliDecoder.TryDecompress(destination.AsSpan(0, length), back, out int backLength);
        yield return "one-shot " + compressedOnce + " " + Fingerprint(destination.AsSpan(0, length)) + " " + decoded + " " + (backLength == input.Length && back.AsSpan().SequenceEqual(input));

        using var encoder = new BrotliEncoder(5, 18);
        var chunks = new MemoryStream();
        var buffer = new byte[4096];
        for (int offset = 0; offset < input.Length; offset += 5000)
        {
            ReadOnlySpan<byte> source = input.AsSpan(offset, Math.Min(5000, input.Length - offset));
            while (true)
            {
                OperationStatus status = encoder.Compress(source, buffer, out int consumed, out int produced, isFinalBlock: false);
                chunks.Write(buffer, 0, produced);
                source = source.Slice(consumed);
                if (status != OperationStatus.DestinationTooSmall && source.IsEmpty)
                {
                    break;
                }
            }
        }
        OperationStatus final;
        do
        {
            final = encoder.Compress(ReadOnlySpan<byte>.Empty, buffer, out _, out int produced, isFinalBlock: true);
            chunks.Write(buffer, 0, produced);
        }
        while (final == OperationStatus.DestinationTooSmall);
        byte[] streamed = chunks.ToArray();
        using var decoder = new BrotliDecoder();
        var decodedOutput = new byte[input.Length];
        OperationStatus result = decoder.Decompress(streamed, decodedOutput, out int read, out int wrote);
        yield return "streaming " + Fingerprint(streamed) + " " + result + " " + read + " " + wrote + " " + decodedOutput.AsSpan().SequenceEqual(input);
    }

    // Deflated ZIP entries at every level, written with fixed times, then read back and updated.
    internal static IEnumerable<string> Zip()
    {
        var archive = new MemoryStream();
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
        {
            int index = 0;
            foreach (CompressionLevel level in new[] { CompressionLevel.Optimal, CompressionLevel.Fastest, CompressionLevel.NoCompression, CompressionLevel.SmallestSize })
            {
                var entry = zip.CreateEntry("entries/" + level + ".bin", level);
                entry.LastWriteTime = EntryTime;
                using var stream = entry.Open();
                stream.Write(Corpus, index * 10_000, 60_000);
                index++;
            }
        }
        byte[] bytes = archive.ToArray();
        yield return "archive " + Fingerprint(bytes);
        using (var zip = new ZipArchive(new MemoryStream(bytes)))
        {
            foreach (var entry in zip.Entries)
            {
                var content = new MemoryStream();
                using (var stream = entry.Open())
                {
                    stream.CopyTo(content);
                }
                yield return entry.FullName + " " + entry.Length + " " + entry.CompressedLength + " " + entry.Crc32.ToString("X8") + " " + Fingerprint(content.ToArray());
            }
        }

        var updated = new MemoryStream();
        updated.Write(bytes);
        using (var zip = new ZipArchive(updated, ZipArchiveMode.Update, leaveOpen: true))
        {
            using (var stream = zip.GetEntry("entries/Optimal.bin")!.Open())
            {
                stream.SetLength(0);
                stream.Write(Encoding.ASCII.GetBytes("rewritten rewritten rewritten"));
            }
            zip.GetEntry("entries/Optimal.bin")!.LastWriteTime = EntryTime;
            var added = zip.CreateEntry("added.txt", CompressionLevel.SmallestSize);
            added.LastWriteTime = EntryTime;
            using (var stream = added.Open())
            {
                stream.Write(Encoding.ASCII.GetBytes("added added added"));
            }
        }
        yield return "updated " + Fingerprint(updated.ToArray());
        using (var zip = new ZipArchive(new MemoryStream(updated.ToArray())))
        {
            using var reader = new StreamReader(zip.GetEntry("entries/Optimal.bin")!.Open());
            using var addedReader = new StreamReader(zip.GetEntry("added.txt")!.Open());
            yield return "reread " + reader.ReadToEnd() + " | " + addedReader.ReadToEnd();
        }
    }
}
