using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.System.IO.Compression.Tests;

// DeflateStream, ZLibStream, GZipStream, the .NET 11 encoders, ZIP's Deflate entries and Brotli are upstream's, over
// the zlib-ng and Brotli releases .NET 11 vendors (2.2.5 and 1.1.0; NetWasm.App.Runtime's
// libSystem.IO.Compression.Native). The expected
// fingerprints are desktop .NET 11's output (CompressionCases), so the netwasm lanes compare byte for byte with it.
public sealed class CompressionTests
{
    private static async Task Matches(IEnumerable<string> observed, params string[] expected) =>
        await Assert.That(string.Join("\n", observed)).IsEqualTo(string.Join("\n", expected));

    // Exception kinds by type test, not by name: a NetWasm program without type names reads a type's ID.
    private static string Failure(Action action)
    {
        try
        {
            action();
            return "none";
        }
        catch (InvalidDataException)
        {
            return nameof(InvalidDataException);
        }
        catch (PlatformNotSupportedException)
        {
            return nameof(PlatformNotSupportedException);
        }
        catch (Exception)
        {
            return "other";
        }
    }

    [Test]
    public async Task StreamsMatchDesktopAtEveryLevel() => await Matches(CompressionCases.Levels(),
        "deflate Optimal 69398:E50B26353A064619",
        "deflate Fastest 75313:05FC8054AE1B4341",
        "deflate NoCompression 262189:D45FC32010738331",
        "deflate SmallestSize 68605:5E92E6A1D5C42432",
        "zlib Optimal 69404:A951FFFC3DD36C79",
        "zlib Fastest 75319:C039D8F45B49E5E0",
        "zlib NoCompression 262195:5B6ED8B28F439D7C",
        "zlib SmallestSize 68611:606D9F6C834DB118",
        "gzip Optimal 69416:62235DB3A6D9F7EE",
        "gzip Fastest 75331:A64DCF528CF365DC",
        "gzip NoCompression 262207:5D6A737E1D5A2238",
        "gzip SmallestSize 68623:04F6DA37BA842145");

    [Test]
    public async Task ZLibLevelsAndStrategiesMatchDesktop() => await Matches(CompressionCases.Options(),
        "level 0 262195:5B6ED8B28F439D7C",
        "level 1 75319:C039D8F45B49E5E0",
        "level 2 72808:C5C141FB625A3A3F",
        "level 3 71297:30E3495FFB4D5FD3",
        "level 4 71219:06607E69B1DA5F1F",
        "level 5 70057:9EE47B06AD13C164",
        "level 6 69404:A951FFFC3DD36C79",
        "level 7 69421:4287C2F1A2C6BCDF",
        "level 8 68606:96CE0A5399A7F14F",
        "level 9 68611:606D9F6C834DB118",
        "strategy Filtered 69398:E50B26353A064619",
        "strategy HuffmanOnly 199367:F987A690C7DD3ACF",
        "strategy RunLengthEncoding 199367:F987A690C7DD3ACF",
        "strategy Fixed 72391:DBF5792369E71D28");

    [Test]
    public async Task FlushWritesASyncFlushBlock() => await Matches(CompressionCases.SyncFlush(),
        "flushed 15:4C15A3C4991029E1 0000FFFF",
        "whole 23:3AA67FEFB4DFBCF4 hello hello hello world");

    [Test]
    public async Task GZipMembersConcatenateAndTrailingDataStays() => await Matches(CompressionCases.Framing(),
        "members first member, second member",
        "trailing payload payload payload payload=12 rest=TRAILER");

    [Test]
    public async Task EncodersAndDecodersMatchDesktop() => await Matches(CompressionCases.Encoders(),
        "deflate one-shot True 12977:5CF7EAD0B2E21D56",
        "deflate restore True True",
        "gzip encoder Done 50000 14165:65CD3FC26C016B51",
        "gzip decoder Done 14165 50000 True",
        "zlib one-shot True 13124:20C4D93E0855D145");

    // Brotli is upstream's 1.1.0, as .NET 11 vendors it (upstream's own WASM builds leave it out).
    [Test]
    public async Task BrotliMatchesDesktop() => await Matches(CompressionCases.Brotli(),
        "stream Optimal 69311:7609143E2703E1EF True",
        "stream Fastest 79159:1EBBE38B7F7DE48E True",
        "stream NoCompression 84800:D0B0C09F808CC38A True",
        "stream SmallestSize 67816:C687447101FFD16F True",
        "quality 0 window 10 True 28877:31C2E93772C014A8",
        "quality 0 window 22 True 18554:CA69A3881D2ABC32",
        "quality 1 window 10 True 26655:233026120E1CA5CD",
        "quality 1 window 22 True 16351:1EB06B71120C8FD6",
        "quality 2 window 10 True 16554:C21158394038BCE8",
        "quality 2 window 22 True 16324:9882E9CA0CD541C8",
        "quality 3 window 10 True 16092:208DA3D671A0C68D",
        "quality 3 window 22 True 15943:C725461D29F18869",
        "quality 4 window 10 True 15733:0A0B3A2CA9858C7C",
        "quality 4 window 22 True 15667:F8548784F164FC08",
        "quality 5 window 10 True 15561:6B1BB907558235EC",
        "quality 5 window 22 True 15488:B857E99900BDF3AD",
        "quality 6 window 10 True 15561:6B1BB907558235EC",
        "quality 6 window 22 True 15458:807FFA56F98338D0",
        "quality 7 window 10 True 15562:7D2337C114C1DF4D",
        "quality 7 window 22 True 15412:68EEE5EE453E9201",
        "quality 8 window 10 True 15562:7D2337C114C1DF4D",
        "quality 8 window 22 True 15408:139AEF88A8E53BB2",
        "quality 9 window 10 True 15406:064E28EDFE27B5D2",
        "quality 9 window 22 True 15259:69B0962EA84ADAE6",
        "quality 10 window 10 True 15634:483D4A6755F76ED4",
        "quality 10 window 22 True 15577:E5869DDB3C7717BC",
        "quality 11 window 10 True 15357:6F40084E7CE56C4F",
        "quality 11 window 22 True 15299:1CE4ACAC64BB4CF6",
        "one-shot True 15667:F8548784F164FC08 True True",
        "streaming 15488:D045DEE464BF3CB5 Done 15488 64000 True");

    [Test]
    public async Task DeflatedZipEntriesMatchDesktop() => await Matches(CompressionCases.Zip(),
        "archive 105451:3830386519C9213B",
        "entries/Optimal.bin 60000 14891 2430511C 60000:E68456F5E4AC0994",
        "entries/Fastest.bin 60000 14809 DAF57771 60000:3A36084E21EEC778",
        "entries/NoCompression.bin 60000 60000 0A8D0641 60000:6D3EA94F227A1691",
        "entries/SmallestSize.bin 60000 15251 F0E9C2FD 60000:9C5540E746188BA3",
        "updated 90679:675A12A5AF4CA6CB",
        "reread rewritten rewritten rewritten | added added added");

    [Test]
    public async Task EveryFormatAndLevelRoundTrips()
    {
        foreach (string format in CompressionCases.Formats)
        {
            foreach (CompressionLevel level in new[] { CompressionLevel.Optimal, CompressionLevel.Fastest, CompressionLevel.NoCompression, CompressionLevel.SmallestSize })
            {
                byte[] restored = CompressionCases.Decompress(format, CompressionCases.Compress(format, level, CompressionCases.Corpus));
                await Assert.That(restored.AsSpan().SequenceEqual(CompressionCases.Corpus)).IsTrue();
            }
        }
    }

    [Test]
    public async Task AsynchronousStreamsMatchSynchronousOnes()
    {
        var output = new MemoryStream();
        await using (var stream = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            for (int i = 0; i < CompressionCases.Corpus.Length; i += 7919)
            {
                await stream.WriteAsync(CompressionCases.Corpus.AsMemory(i, Math.Min(7919, CompressionCases.Corpus.Length - i)));
            }
        }
        await Assert.That(CompressionCases.Fingerprint(output.ToArray())).IsEqualTo("69416:62235DB3A6D9F7EE");

        output.Position = 0;
        var restored = new MemoryStream();
        await using (var stream = new GZipStream(output, CompressionMode.Decompress))
        {
            await stream.CopyToAsync(restored);
        }
        await Assert.That(restored.ToArray().AsSpan().SequenceEqual(CompressionCases.Corpus)).IsTrue();
    }

    [Test]
    public async Task CorruptInputThrowsInvalidDataException()
    {
        byte[] noise = CompressionCases.Corpus.AsSpan(1000, 4000).ToArray();
        noise[0] = 0xFF;
        await Assert.That(Failure(() => CompressionCases.Decompress("deflate", noise))).IsEqualTo(nameof(InvalidDataException));
        await Assert.That(Failure(() => CompressionCases.Decompress("gzip", Encoding.ASCII.GetBytes("not a gzip stream at all")))).IsEqualTo(nameof(InvalidDataException));
        byte[] truncated = CompressionCases.Compress("zlib", CompressionLevel.Optimal, CompressionCases.Corpus).AsSpan(0, 30_000).ToArray();
        byte[] partial = [];
        await Assert.That(Failure(() => partial = CompressionCases.Decompress("zlib", truncated))).IsEqualTo("none");
        await Assert.That(partial.Length > 0 && partial.AsSpan().SequenceEqual(CompressionCases.Corpus.AsSpan(0, partial.Length))).IsTrue();
    }

    [Test]
    public async Task ZstandardIsUpstreamsWasiBuild()
    {
#if NETWASM
        // Upstream's browser and WASI builds have no Zstandard codec.
        await Assert.That(Failure(() => _ = new ZstandardCompressionOptions())).IsEqualTo(nameof(PlatformNotSupportedException));
        await Assert.That(Failure(() => _ = new ZstandardStream(new MemoryStream(), CompressionMode.Compress))).IsEqualTo(nameof(PlatformNotSupportedException));
#else
        var output = new MemoryStream();
        using (var stream = new ZstandardStream(output, CompressionMode.Compress, leaveOpen: true))
        {
            stream.Write(CompressionCases.Corpus);
        }
        output.Position = 0;
        var restored = new MemoryStream();
        using (var stream = new ZstandardStream(output, CompressionMode.Decompress))
        {
            stream.CopyTo(restored);
        }
        await Assert.That(restored.ToArray().AsSpan().SequenceEqual(CompressionCases.Corpus)).IsTrue();
#endif
    }
}
