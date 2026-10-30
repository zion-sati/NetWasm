// Compatibility cases adapted from dotnet/runtime System.Runtime.Tests GuidTests.cs.
// Upstream commit: 474ed963699fc29809015392738a15231aaae2de.

using NetWasm.TestInfrastructure;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class GuidCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerPreservesGuidByteOrderRoundTrips(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var source =
            """
            namespace GuidByteOrderFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var bigEndian = new byte[]
                    {
                        0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77,
                        0x88, 0x99, 0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0xff,
                    };
                    var littleEndian = new byte[]
                    {
                        0x33, 0x22, 0x11, 0x00, 0x55, 0x44, 0x77, 0x66,
                        0x88, 0x99, 0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0xff,
                    };

                    var fromBigEndian = new System.Guid(bigEndian, bigEndian: true);
                    var fromLittleEndian = new System.Guid(littleEndian, bigEndian: false);
                    if (fromBigEndian != fromLittleEndian) return 1;
                    if (fromBigEndian.ToString() != "00112233-4455-6677-8899-aabbccddeeff") return 2;

                    var bigRoundTrip = fromBigEndian.ToByteArray(bigEndian: true);
                    var littleRoundTrip = fromBigEndian.ToByteArray(bigEndian: false);
                    for (var i = 0; i < 16; i++)
                    {
                        if (bigRoundTrip[i] != bigEndian[i]) return 3;
                        if (littleRoundTrip[i] != littleEndian[i]) return 4;
                    }

                    System.Span<byte> destination = stackalloc byte[16];
                    if (!fromBigEndian.TryWriteBytes(destination, bigEndian: true, out var written) ||
                        written != 16)
                        return 5;
                    for (var i = 0; i < 16; i++)
                    {
                        if (destination[i] != bigEndian[i]) return 6;
                    }

                    System.Span<byte> shortDestination = stackalloc byte[15];
                    if (fromBigEndian.TryWriteBytes(shortDestination, bigEndian: true, out written) ||
                        written != 0)
                        return 7;

                    return input;
                }
            }
            """;
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            "GuidByteOrderFixture",
            source,
            "GuidByteOrderFixture.EntryPoint",
            optimize,
            target,
            42,
            []));

        Assert.Equal(42, result);
    }

    [Fact]
    public void CompilerRequiresASelectedWitContractForReachablePlatformImports()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "GuidCapabilityFixture",
            """
            namespace GuidCapabilityFixture;

            public static class EntryPoint
            {
                public static int Run(int input) => System.Guid.NewGuid().Version + input;
            }
            """);

        var absent = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(
            new CompilerOptions(
                assembly,
                [assets.CoreLib],
                "GuidCapabilityFixture.EntryPoint",
                "Run",
                [])));
        Assert.Equal(DiagnosticCode.ComponentContract, absent.Diagnostic.Code);
        Assert.Equal(
            "reachable WIT imports or exports require '--wit' and a selected WIT world",
            absent.Diagnostic.Message);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerPreservesVersion7TimestampAndVersion4RandomBits(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var source =
            """
            namespace GuidCreationFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var timestamp = System.DateTimeOffset.FromUnixTimeMilliseconds(0x0123456789ab);
                    var version7 = System.Guid.CreateVersion7(timestamp);
                    var version7Text = version7.ToString();
                    var currentVersion7 = System.Guid.CreateVersion7();
                    var version4 = System.Guid.NewGuid();

                    var validVersion7Variant = version7.Variant is >= 8 and <= 11;
                    var validVersion4Variant = version4.Variant is >= 8 and <= 11;
                    var timestampPrefix =
                        version7Text[0] == '0' &&
                        version7Text[1] == '1' &&
                        version7Text[2] == '2' &&
                        version7Text[3] == '3' &&
                        version7Text[4] == '4' &&
                        version7Text[5] == '5' &&
                        version7Text[6] == '6' &&
                        version7Text[7] == '7' &&
                        version7Text[9] == '8' &&
                        version7Text[10] == '9' &&
                        version7Text[11] == 'a' &&
                        version7Text[12] == 'b';

                    var preEpochThrows = false;
                    try
                    {
                        _ = System.Guid.CreateVersion7(
                            System.DateTimeOffset.UnixEpoch - System.TimeSpan.FromMilliseconds(1));
                    }
                    catch (System.ArgumentOutOfRangeException)
                    {
                        preEpochThrows = true;
                    }

                    return (version7.Version == 7 ? 1 : 0) |
                        (validVersion7Variant ? 2 : 0) |
                        (timestampPrefix ? 4 : 0) |
                        (version4.Version == 4 ? 8 : 0) |
                        (validVersion4Variant ? 16 : 0) |
                        (preEpochThrows ? 32 : 0) |
                        (version4 != version7 ? 64 : 0) |
                        (currentVersion7.Version == 7 &&
                            currentVersion7.Variant is >= 8 and <= 11 ? 128 : 0) |
                        input;
                }
            }
            """;
        var assembly = optimize
            ? assets.CompileOptimizedSource("GuidCreationFixture", source)
            : assets.CompileSource(
            "GuidCreationFixture",
            source);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "GuidCreationFixture.EntryPoint",
            "Run",
            [new RequestedExport(
                "run",
                "GuidCreationFixture.EntryPoint",
                "Run")],
            target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));

        ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Contains(result.Program.WitImportMethods, method =>
            method.WitImport is
            {
                InterfaceName: "wasi:random/random@0.2.11",
                FunctionName: "get-random-bytes",
            });
        Assert.DoesNotContain(result.Program.WitImportMethods, method =>
            method.WitImport?.InterfaceName == "wasi:random/random@0.2.11" &&
            method.WitImport.FunctionName == "get-random-u64");
        Assert.Equal(255, ExecuteWithStandardWasiNode(
            result.ApplicationModule,
            assets.Directory,
            0,
            target,
            result.StaticDataEnd));
    }

    [Fact]
    public void CompilerTrimsUnreachableRandomnessCapability()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "GuidTrimmingFixture",
            """
            namespace GuidTrimmingFixture;

            public static class EntryPoint
            {
                public static int Run(int input) => input;
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "GuidTrimmingFixture.EntryPoint",
            "Run",
            [],
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));

        Assert.DoesNotContain(result.Program.WitImportMethods, method =>
            method.WitImport?.InterfaceName == "wasi:random/random@0.2.11");
    }
}
