using NetWasm.Compiler.Core;
using NetWasm.Compiler.ComponentModel;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// The codecs' native entry points stay out of a program that does not use them. A program that writes to the console
// reaches Stream.Write virtually, so every allocated stream's Write override is reachable; a stream that only
// decompresses must still not reach its encoder, nor one that only compresses its decoder. The harness does not link
// native archives, so each native call a program reaches is an import of the emitted module, named after its entry
// point, and the tests read those imports. Release builds (O2, O3, Os, Oz) propagate the constant compression mode
// into the shared constructors, which is what lets the other direction's setup fall away.
public sealed class CompressionPayForUseCompilationTests
{
    private static readonly string[] DeflateEncoder =
        ["CompressionNative_DeflateInit2_", "CompressionNative_Deflate", "CompressionNative_DeflateEnd"];
    private static readonly string[] DeflateDecoder =
        ["CompressionNative_InflateInit2_", "CompressionNative_Inflate", "CompressionNative_InflateEnd"];
    private static readonly string[] BrotliEncoder =
        ["BrotliEncoderCreateInstance", "BrotliEncoderCompressStream", "BrotliEncoderDestroyInstance"];
    private static readonly string[] BrotliDecoder =
        ["BrotliDecoderCreateInstance", "BrotliDecoderDecompressStream", "BrotliDecoderDestroyInstance"];

    [Theory]
    [InlineData("gzip-decompress", "GZipDecompress", true)]
    [InlineData("gzip-compress", "GZipCompress", true)]
    [InlineData("brotli-decompress", "BrotliDecompress", true)]
    [InlineData("brotli-compress", "BrotliCompress", true)]
    [InlineData("none", "None", false)]
    public void ProgramsImportOnlyTheCodecDirectionTheyUse(string scenario, string method, bool usesCodec)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("CompressionPayForUseFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "CompressionPayForUseFixture.EntryPoint",
            method, [new("run", "CompressionPayForUseFixture.EntryPoint", method)], Target: WasmTarget.Wasm32,
            Optimization: FinalWasmOptimization.Oz,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        var imports = WasmModuleInspection.ReadFunctionImportNames(result.ApplicationModule);
        string[] used = scenario switch
        {
            "gzip-decompress" => DeflateDecoder,
            "gzip-compress" => DeflateEncoder,
            "brotli-decompress" => BrotliDecoder,
            "brotli-compress" => BrotliEncoder,
            _ => [],
        };
        string[] unused = [.. DeflateEncoder, .. DeflateDecoder, .. BrotliEncoder, .. BrotliDecoder];
        foreach (var name in used)
        {
            Assert.Contains(name, imports);
        }
        // zlib's DeflateEnd and InflateEnd are exempt: upstream's ZLibStreamHandle releases either direction from one
        // ReleaseHandle, so a stream of one direction keeps the other's End (176 bytes of native code for
        // DeflateEnd, against about 11 KB for deflate itself).
        foreach (var name in unused.Except(used).Except(["CompressionNative_DeflateEnd", "CompressionNative_InflateEnd"]))
        {
            Assert.DoesNotContain(name, imports);
        }
        Assert.Equal(usesCodec, imports.Any(name => name.StartsWith("CompressionNative_", StringComparison.Ordinal) ||
            name.StartsWith("Brotli", StringComparison.Ordinal)));
    }

    internal const string Source = """
        using System;
        using System.IO;
        using System.IO.Compression;
        using System.Text;
        namespace CompressionPayForUseFixture;

        public static class EntryPoint
        {
            // Each writes to the console, which reaches Stream.Write virtually, and reads a stream through a
            // StreamReader, which reaches Stream.Read virtually.
            public static int GZipDecompress(int input)
            {
                var data = new MemoryStream(Convert.FromBase64String("H4sIAAAAAAAAA8tIzcnJV8hAIvNSS8oTi3MBsn0xoRkAAAA="));
                using var reader = new StreamReader(new GZipStream(data, CompressionMode.Decompress));
                Console.WriteLine(reader.ReadToEnd());
                return input + 1;
            }

            public static int GZipCompress(int input)
            {
                var output = new MemoryStream();
                using (var stream = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
                {
                    stream.Write(Encoding.ASCII.GetBytes("hello"));
                }
                Console.WriteLine(new StreamReader(new MemoryStream(output.ToArray())).ReadToEnd().Length);
                return input + 1;
            }

            public static int BrotliDecompress(int input)
            {
                var data = new MemoryStream(Convert.FromBase64String("GxgAAMRtbF33dQgOq1FQyRJPHLGSga17bgE="));
                using var reader = new StreamReader(new BrotliStream(data, CompressionMode.Decompress));
                Console.WriteLine(reader.ReadToEnd());
                return input + 1;
            }

            public static int BrotliCompress(int input)
            {
                var output = new MemoryStream();
                using (var stream = new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true))
                {
                    stream.Write(Encoding.ASCII.GetBytes("hello"));
                }
                Console.WriteLine(new StreamReader(new MemoryStream(output.ToArray())).ReadToEnd().Length);
                return input + 1;
            }

            public static int None(int input)
            {
                Console.WriteLine(new StreamReader(new MemoryStream(Encoding.ASCII.GetBytes("plain"))).ReadToEnd());
                return input + 1;
            }
        }
        """;
}
