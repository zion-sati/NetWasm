// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// A vector local written through its address, as a field store, a compound
// assignment, an indexer, a ref local or a field of a struct local, stays a
// v128 value, as RyuJIT keeps a promoted SIMD local in a register: no method
// below needs a value frame, except the one that passes its local's address to
// a call that is not inlined.
public sealed partial class VectorLocalFrameCompilationTests
{
    internal const string Source = """
        using System;
        using System.Numerics;
        using System.Runtime.CompilerServices;
        using System.Runtime.Intrinsics;

        public struct Particle
        {
            public int Id;
            public Vector3 Position;
            public float Mass;
        }

        public static class EntryPoint
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static float FieldStore(float a)
            {
                var v = new Vector4(a, a + 1, a + 2, a + 3);
                v.Y = a * 2;
                v.W = MathF.Sqrt(a + 9);
                return v.X + v.Y * 3 + v.Z * 5 + v.W * 7;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static float Compound(float a)
            {
                var v = new Vector3(a);
                v.Z += 1;
                v.X *= 2;
                v.Y -= v.X;
                return v.Length() + v.X + v.Y * 3 + v.Z * 5;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static float PairStores(float a)
            {
                Vector2 v = default;
                v.X = a;
                v.Y = a + 1;
                return v.X * v.Y + Vector2.Dot(v, v);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static float Accumulate(float[] values)
            {
                var acc = Vector4.Zero;
                for (var i = 0; i < values.Length; i++)
                {
                    acc.X += values[i];
                    acc.Y = MathF.Max(acc.Y, values[i]);
                    acc.W += 1;
                }
                return acc.X + acc.Y * 3 + acc.W * 7;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static float StructField(float a)
            {
                var p = new Particle { Id = 3, Position = new Vector3(a, 2, 3), Mass = 4 };
                p.Position.X = a + 1;
                p.Position.Y += p.Mass;
                return p.Position.X + p.Position.Y * 3 + p.Position.Z * 5 + p.Id;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static float RefLocal(float a)
            {
                var v = new Vector4(a);
                ref var r = ref v;
                r.X = a + 5;
                r.Z += 1;
                return v.X + v.Y * 3 + v.Z * 5 + v.W * 7;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static float Indexer(float a)
            {
                var v = new Vector4(a);
                v[2] = a + 4;
                v[0] += 1;
                return v[0] + v[1] * 3 + v[2] * 5 + v[3] * 7;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static float View(float a)
            {
                var v = new Vector4(a);
                Unsafe.As<Vector4, Vector128<float>>(ref v) = Vector128.Create(a, a * 2, a * 3, a * 4);
                v.Z = 1;
                return v.X + v.Y * 3 + v.Z * 5 + v.W * 7;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static float Sum(in Vector4 v) => v.X + v.Y * 3 + v.Z * 5 + v.W * 7;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static void Bump(ref Vector4 v, float by) => v.Y += by;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static float InlinedByReference(float a)
            {
                var v = new Vector4(a);
                Bump(ref v, 2);
                v.X = Sum(in v);
                return Sum(in v);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Escape(ref Vector4 v) => v.X += 10;

            // The address goes to a call that is not inlined: the local stays in memory.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static float Escaping(float a)
            {
                var v = new Vector4(a);
                v.Y = a + 1;
                Escape(ref v);
                v.Z = v.X;
                return v.X + v.Y * 3 + v.Z * 5 + v.W * 7;
            }

            public static int Run(int input)
            {
                var a = input * 0.25f + 0.5f;
                var values = new float[input % 5 + 3];
                for (var i = 0; i < values.Length; i++) values[i] = i * 1.5f - input;
                var total = FieldStore(a) + Compound(a) + PairStores(a) + Accumulate(values) + StructField(a) + RefLocal(a) +
                    Indexer(a) + View(a) + InlinedByReference(a) + Escaping(a);
                return (int)(total * 16);
            }
        }
        """;

    private static readonly string[] Promoted =
    [
        "$EntryPoint::FieldStore", "$EntryPoint::Compound", "$EntryPoint::PairStores", "$EntryPoint::Accumulate",
        "$EntryPoint::StructField", "$EntryPoint::RefLocal", "$EntryPoint::Indexer", "$EntryPoint::View",
        "$EntryPoint::InlinedByReference",
    ];

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    public void VectorLocalsWrittenThroughTheirAddressNeedNoValueFrame(WasmTarget target, FinalWasmOptimization preset)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource($"VectorLocalFrame_{target}_{preset}", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target, Optimization: preset));

        var module = Path.Combine(assets.Directory, $"application-{Guid.NewGuid():N}.wasm");
        File.WriteAllBytes(module, result.ApplicationModule);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var printed = services.GetRequiredService<IWasmTools>().Run("print", module);
        Assert.True(printed.ExitCode == 0, printed.StandardError);
        var framed = FunctionHeader().Split(printed.StandardOutput).Skip(1)
            .Select(static function => (Name: function[..function.IndexOf(' ', StringComparison.Ordinal)], Body: function))
            .Where(static function => function.Name.StartsWith("$EntryPoint::", StringComparison.Ordinal))
            .ToDictionary(static function => function.Name, static function => FrameEnter().IsMatch(function.Body));
        var report = string.Join(", ", framed.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => $"{pair.Key}={(pair.Value ? "frame" : "none")}"));
        Assert.All(Promoted, probe =>
        {
            Assert.True(framed.TryGetValue(probe, out var frame), $"{probe} is not in the module: {report}");
            Assert.False(frame, $"{probe} enters a value frame: {report}");
        });
        Assert.True(framed.TryGetValue("$EntryPoint::Escaping", out var escaping) && escaping, report);
    }

    [GeneratedRegex(@"\n\s*\(func ")]
    private static partial Regex FunctionHeader();

    [GeneratedRegex(@"call \$\S*value_frame_enter")]
    private static partial Regex FrameEnter();
}
