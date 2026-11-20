// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Tests.Correctness;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// GC.AllocateUninitializedArray follows upstream's policy: element types with references and arrays under 2048
// bytes are allocated zeroed; larger arrays of element types without references take the runtime's uninitialized
// allocation, whose elements are unspecified until written. Only a program that calls it imports that allocation.
public sealed partial class UninitializedArrayCompilationTests(CorrectnessTestRunner runner)
{
    internal const string Source = """
        using System;
        using System.Buffers;
        using System.IO;

        namespace UninitializedArrayFixture;

        public struct Pixel { public byte R, G, B, A; }
        public struct Tagged { public string Name; public int Value; }

        public static class EntryPoint
        {
            private static long Fill<T>(T[] array, Func<int, T> value, Func<T, long> read)
            {
                for (var i = 0; i < array.Length; i++) array[i] = value(i);
                long sum = 0;
                foreach (var item in array) sum += read(item);
                return sum;
            }

            private static bool AllDefault<T>(T[] array)
            {
                foreach (var item in array) if (!Equals(item, default(T))) return false;
                return true;
            }

            public static int Run(int input)
            {
                long total = 0;
                foreach (var length in new[] { 0, 1, 100, 2047, 2048, 4096, 100_000, 1 << 20 })
                {
                    var bytes = GC.AllocateUninitializedArray<byte>(length);
                    if (bytes.Length != length || bytes.GetType() != typeof(byte[])) return 1;
                    total += Fill(bytes, i => (byte)(i * 7), b => b);
                    // Arrays under 2048 bytes are zeroed, as upstream allocates them.
                    if (length < 2048 && !AllDefault(GC.AllocateUninitializedArray<byte>(length))) return 2;
                }
                var ints = GC.AllocateUninitializedArray<int>(50_000, pinned: true);
                total += Fill(ints, i => i - 25_000, i => i);
                var pixels = GC.AllocateUninitializedArray<Pixel>(10_000);
                total += Fill(pixels, i => new Pixel { R = (byte)i, A = 255 }, p => p.R + p.A);
                // Element types with references are always zeroed.
                var names = GC.AllocateUninitializedArray<string>(5_000);
                if (!AllDefault(names)) return 3;
                var tagged = GC.AllocateUninitializedArray<Tagged>(5_000);
                foreach (var item in tagged) if (item.Name is not null || item.Value != 0) return 4;
                try { GC.AllocateUninitializedArray<byte>(-1); return 5; } catch (OverflowException) { }
                try { GC.AllocateUninitializedArray<long>(-input); return 6; } catch (OverflowException) { }

                // The callers that reach it: the shared pool's rentals and MemoryStream.ToArray, across collections.
                for (var round = 0; round < 20; round++)
                {
                    var rented = ArrayPool<byte>.Shared.Rent(65_536 + round);
                    if (rented.Length < 65_536 + round) return 7;
                    rented[round] = (byte)round;
                    total += rented[round];
                    ArrayPool<byte>.Shared.Return(rented);
                    using var stream = new MemoryStream();
                    stream.Write(new byte[4096 + round], 0, 4096 + round);
                    var copy = stream.ToArray();
                    if (copy.Length != 4096 + round || !AllDefault(copy)) return 8;
                    var garbage = new object[1000];
                    for (var i = 0; i < garbage.Length; i++) garbage[i] = new byte[512];
                    GC.Collect();
                }
                return total == ExpectedTotal() ? 42 : 9;
            }

            private static long ExpectedTotal()
            {
                long total = 0;
                foreach (var length in new[] { 0, 1, 100, 2047, 2048, 4096, 100_000, 1 << 20 })
                    for (var i = 0; i < length; i++) total += (byte)(i * 7);
                for (var i = 0; i < 50_000; i++) total += i - 25_000;
                for (var i = 0; i < 10_000; i++) total += (byte)i + 255;
                for (var round = 0; round < 20; round++) total += round;
                return total;
            }
        }
        """;

    [Theory]
    [InlineData("Debug-Wasm32-Direct-Linked")]
    [InlineData("Release-Wasm32-Direct-Linked")]
    [InlineData("Debug-Wasm64-Direct-Linked")]
    [InlineData("Release-Wasm64-Direct-Linked")]
    public void AllocatesAsDesktopOnTheNativeRuntime(string cell)
    {
        var fixture = new CorpusFixture("UninitializedArrayFixture", "UninitializedArrayFixture", Source, [41])
        {
            CaseId = "gc.allocate-uninitialized-array",
            ExpectedReturnValue = 42,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection,
            Matrix = new(CorpusMatrixProfile.Extended, cell, CorpusExecutionBackend.Linked),
            ExposesLegacyTrace = false,
            OracleMode = OracleMode.SameSource,
            SameSourceReason = "Compare GC.AllocateUninitializedArray and its callers against the desktop runtime.",
            Optimization = FinalWasmOptimization.None,
        };
        runner.Run(fixture);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void OnlyAProgramThatCallsItImportsTheUninitializedAllocation(WasmTarget target)
    {
        Assert.True(ImportsUninitializedAllocation("""
            public static class EntryPoint
            {
                public static int Run(int input) => System.GC.AllocateUninitializedArray<byte>(input * 1000).Length == 41_000 ? 42 : 1;
            }
            """, target));
        Assert.False(ImportsUninitializedAllocation("""
            public static class EntryPoint
            {
                public static int Run(int input) => new byte[input * 1000].Length == 41_000 ? 42 : 1;
            }
            """, target));
    }

    private static bool ImportsUninitializedAllocation(string source, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("UninitializedArrayReach", source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target, Optimization: FinalWasmOptimization.O0));
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, 41, target, result.StaticDataEnd));
        var path = Path.Combine(assets.Directory, "application.wasm");
        File.WriteAllBytes(path, result.ApplicationModule);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var printed = services.GetRequiredService<IWasmTools>().Run("print", path);
        Assert.True(printed.ExitCode == 0, printed.StandardError);
        return UninitializedImport().IsMatch(printed.StandardOutput);
    }

    [GeneratedRegex(@"\(import ""[^""]+"" ""allocate_uninitialized_value_array""")]
    private static partial Regex UninitializedImport();
}
