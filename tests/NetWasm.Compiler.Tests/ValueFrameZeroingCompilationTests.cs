// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Tests.Correctness;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// Under SkipLocalsInit a value-frame local that holds no reference starts with what its frame held, as on .NET,
// and is not zeroed at entry; one that holds a reference or a byref is still zeroed, because the collector traces it
// wherever its address reaches, as CoreCLR zeroes GC slots whatever the flag says. Under localsinit every local is
// zeroed.
public sealed partial class ValueFrameZeroingCompilationTests(CorrectnessTestRunner runner)
{
    internal const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        [module: SkipLocalsInit]

        namespace FrameZeroingFixture;

        public struct Plain { public long A, B, C, D, E, F, G, H; }
        public struct Holder { public object First; public string Second; public long Count; }

        public static class EntryPoint
        {
            private static object[] keep = new object[64];

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long Use(ref Plain plain) => plain.A + plain.B + plain.C + plain.D + plain.E + plain.F + plain.G + plain.H;

            // Leaves a frame full of non-zero words where the next calls' frames go.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long Pollute()
            {
                Plain first, second, third;
                first.A = first.B = first.C = first.D = first.E = first.F = first.G = first.H = 0x5A5A5A5A5A5A5A5A;
                second = first;
                third = second;
                return Use(ref first) ^ Use(ref second) ^ Use(ref third);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long PlainLocal(int seed)
            {
                Plain plain;
                plain.A = seed; plain.B = seed * 2; plain.C = seed * 3; plain.D = seed * 4;
                plain.E = seed * 5; plain.F = seed * 6; plain.G = seed * 7; plain.H = seed * 8;
                return Use(ref plain);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Churn(int round)
            {
                for (var i = 0; i < 2000; i++) keep[i % keep.Length] = new byte[64 + round];
            }

            // The holder's storage is a root from the call on, and collections run before it is written.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Fill(ref Holder holder, int round)
            {
                Churn(round);
                holder.First = new int[round + 1];
                holder.Second = "round " + round;
                holder.Count = round;
                Churn(round);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int HolderLocal(int round)
            {
                Unsafe.SkipInit(out Holder holder);
                Fill(ref holder, round);
                return ((int[])holder.First).Length + holder.Second.Length + (int)holder.Count;
            }

            // Leaves a reference in a frame holder where the next call's holder goes, and returns a weak
            // reference to what it held.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static WeakReference LeaveReference()
            {
                Holder holder;
                holder.First = new int[] { 7 };
                holder.Second = "stale";
                holder.Count = 1;
                Fill(ref holder, 0);
                holder.First = new int[] { 7 };
                return new WeakReference(holder.First);
            }

            // A collection while the holder, not yet written, is a root: what an earlier frame left in it
            // must not keep an object alive.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static bool StaleReferenceSurvives(WeakReference weak)
            {
                Unsafe.SkipInit(out Holder holder);
                return CollectBeforeWriting(ref holder, weak);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static bool CollectBeforeWriting(ref Holder holder, WeakReference weak)
            {
                GC.Collect();
                GC.Collect();
                var alive = weak.IsAlive;
                holder.First = null!;
                holder.Second = "";
                holder.Count = 0;
                return alive;
            }

            public static int Run(int input)
            {
                if (StaleReferenceSurvives(LeaveReference())) return 7;
                long total = 0;
                for (var round = 0; round < 6; round++)
                {
                    total += Pollute() & 0xFF;
                    total += PlainLocal(input + round);
                    total += Pollute() & 0xFF;
                    total += HolderLocal(round);
                }
                return total == ExpectedTotal(input) ? 42 : (int)(total & 0x7FFFFFFF);
            }

            private static long ExpectedTotal(int input)
            {
                long total = 0;
                for (var round = 0; round < 6; round++)
                {
                    // Pollute's three sums of eight equal words, combined by exclusive or, are one sum.
                    total += 2 * (unchecked(0x5A5A5A5A5A5A5A5AL * 8) & 0xFF);
                    total += 36L * (input + round);
                    total += round + 1 + ("round " + round).Length + round;
                }
                return total;
            }
        }
        """;

    [Theory]
    [InlineData("Debug-Wasm32-Direct-Linked")]
    [InlineData("Release-Wasm32-Direct-Linked")]
    [InlineData("Debug-Wasm64-Direct-Linked")]
    [InlineData("Release-Wasm64-Direct-Linked")]
    public void SkipLocalsInitLeavesPlainLocalsAndZeroesTracedOnes(string cell)
    {
        var fixture = new CorpusFixture("FrameZeroingFixture", "FrameZeroingFixture", Source, [41])
        {
            CaseId = "frame.skiplocalsinit-zeroing",
            ExpectedReturnValue = 42,
            AllowUnsafe = true,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection,
            Matrix = new(CorpusMatrixProfile.Extended, cell, CorpusExecutionBackend.Linked),
            ExposesLegacyTrace = false,
            OracleMode = OracleMode.SameSource,
            SameSourceReason = "Compare SkipLocalsInit frames, with collections over reference-holding locals, against the desktop runtime.",
            Optimization = FinalWasmOptimization.None,
        };
        runner.Run(fixture);
    }

    // The entry of a method with a 64-byte reference-free local zeroes it under localsinit only; one with a
    // reference-holding local zeroes it either way.
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void OnlyLocalsInitZeroesReferenceFreeLocals(WasmTarget target)
    {
        var skipped = EntryZeroStores(Source, target);
        var initialized = EntryZeroStores(Source.Replace("[module: SkipLocalsInit]", "", StringComparison.Ordinal), target);
        Assert.Equal(0, skipped["PlainLocal"]);
        Assert.True(initialized["PlainLocal"] >= 4, $"PlainLocal under localsinit: {initialized["PlainLocal"]} zero stores");
        Assert.True(skipped["HolderLocal"] > 0, "HolderLocal under SkipLocalsInit zeroes its references");
        Assert.Equal(initialized["HolderLocal"], skipped["HolderLocal"]);
    }

    // The zero v128 stores in each method, which is how a frame's entry clears up to 256 bytes.
    private static Dictionary<string, int> EntryZeroStores(string source, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedUnsafeSource("FrameZeroing", source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "FrameZeroingFixture.EntryPoint", "Run", [], Target: target,
            Optimization: FinalWasmOptimization.O0));
        var path = Path.Combine(assets.Directory, "application.wasm");
        File.WriteAllBytes(path, result.ApplicationModule);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var printed = services.GetRequiredService<IWasmTools>().Run("print", path);
        Assert.True(printed.ExitCode == 0, printed.StandardError);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var method in new[] { "PlainLocal", "HolderLocal" })
        {
            var start = FunctionStart(method).Match(printed.StandardOutput);
            Assert.True(start.Success, method);
            var end = printed.StandardOutput.IndexOf("\n  (func ", start.Index + 1, StringComparison.Ordinal);
            var body = printed.StandardOutput[start.Index..(end < 0 ? printed.StandardOutput.Length : end)];
            counts[method] = ZeroVectorStore().Count(body);
        }
        return counts;
    }

    private static Regex FunctionStart(string method) =>
        new($@"\(func \$""?[^ ""]*EntryPoint::{method}\b", RegexOptions.CultureInvariant);

    [GeneratedRegex(@"v128\.const i32x4 0x00000000 0x00000000 0x00000000 0x00000000\s*\n\s*v128\.store")]
    private static partial Regex ZeroVectorStore();
}
