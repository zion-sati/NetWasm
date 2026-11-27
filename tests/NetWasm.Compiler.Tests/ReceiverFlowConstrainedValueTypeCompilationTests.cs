// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;
using static NetWasm.Compiler.Tests.CompilerTestSupport;

namespace NetWasm.Compiler.Tests;

// A virtual call constrained to a value type (object's methods on a generic T that is a struct) has its target picked
// by the type: no receiver reaches it as an object the receiver flow follows, so the flow must not wait for one.
// Structs with and without overrides, as ImmutableHashSet's and ImmutableList's enumerators reach Requires' helpers.
public sealed class ReceiverFlowConstrainedValueTypeCompilationTests
{
    internal const string Source = """
        public struct Point { public int X; public override string ToString() => "P" + X; }
        public struct Plain { public int Y; }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var point = new Point { X = input };
                var one = new Plain { Y = 1 };
                var two = new Plain { Y = 2 };
                return Describe(point).Length * 100 + (Same(one, one) ? 10 : 0) + (Same(one, two) ? 1 : 0) +
                    (Same(point, point) ? 1000 : 0);
            }

            private static string Describe<T>(T value) => value!.ToString()!;

            private static bool Same<T>(T left, T right) => left!.Equals(right);
        }
        """;

    [Theory]
    [InlineData(FinalWasmOptimization.None)]
    [InlineData(FinalWasmOptimization.O1)]
    [InlineData(FinalWasmOptimization.O3)]
    [InlineData(FinalWasmOptimization.Oz)]
    public void ObjectMethodsOnAStructTypeArgumentRun(FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("ReceiverFlowConstrainedValueTypeFixture", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Optimization: optimization));

        Assert.Equal(1210, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 7,
            WasmTarget.Wasm32, result.StaticDataEnd));
    }
}
