// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// A struct argument passed by its fields is stored into its frame copy as the method's value frame is entered, one field
// at a time. A Vector3 field arrives as a v128 and stores 12 bytes, which takes the method's vector scratch: Avalonia's
// Array.IndexOf<KeyFrame<Vector3>> stopped the compile with NW1008 without it, and so did a call taking such a struct
// at None.
public sealed class VectorFieldArgumentCompilationTests
{
    private const string Source = """
        using System;
        using System.Numerics;
        using System.Runtime.CompilerServices;

        public struct KeyFrame<T> : IEquatable<KeyFrame<T>>
        {
            public T Value;
            public float Time;
            public object? Easing;

            public bool Equals(KeyFrame<T> other) => Time == other.Time && Equals(Value, other.Value);
        }

        public static class EntryPoint
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Vector3 Make(int n) => new Vector3(n, n * 2, n * 3);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static float Take(KeyFrame<Vector3> frame) => frame.Value.X + frame.Value.Z + frame.Time;

            public static int Run(int input)
            {
                var frames = new KeyFrame<Vector3>[8];
                for (var index = 0; index < frames.Length; index++)
                    frames[index] = new KeyFrame<Vector3> { Value = Make(index), Time = index };
                var found = Array.IndexOf(frames, frames[input % 8]);
                if (found != input % 8) return 100 + found;
                var taken = Take(new KeyFrame<Vector3> { Value = Make(input), Time = 1 });
                return taken == input * 4 + 1 ? input : 200;
            }
        }
        """;

    [Theory]
    [InlineData(FinalWasmOptimization.None, WasmTarget.Wasm32)]
    [InlineData(FinalWasmOptimization.Oz, WasmTarget.Wasm32)]
    [InlineData(FinalWasmOptimization.O3, WasmTarget.Wasm32)]
    [InlineData(FinalWasmOptimization.None, WasmTarget.Wasm64)]
    [InlineData(FinalWasmOptimization.O3, WasmTarget.Wasm64)]
    public void AStructArgumentWithAVector3FieldIsStoredIntoItsFrame(FinalWasmOptimization optimization, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource($"VectorFieldArgument_{optimization}_{target}", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target, Optimization: optimization));

        Assert.Equal(5, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 5, target, result.StaticDataEnd));
    }
}
