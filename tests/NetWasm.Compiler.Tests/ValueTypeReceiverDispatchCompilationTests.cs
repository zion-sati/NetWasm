using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// ValueType's Equals and GetHashCode compare and hash the boxed value's own fields. Called
// without a constrained prefix that names the type (a struct's base.Equals and base.GetHashCode,
// or the body a delegate to them runs), the call dispatches on the receiver's exact type, always
// to ValueType's method, never to the struct's own override (issue 223's class). Desktop .NET
// gives the same results (MethodGroupTargetCompilationTests' StructBaseCalls and ValueTypeMethods).
public sealed class ValueTypeReceiverDispatchCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    public void BaseCallsAndMethodGroupsOfValueTypeEqualityUseTheReceiversFields(
        WasmTarget target, FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("ValueTypeReceivers_" + target + optimization, """
            using System;
            using System.Runtime.CompilerServices;

            public struct Overriding
            {
                public int Value;
                public string Text;
                public override bool Equals(object? other) => base.Equals(other);
                public override int GetHashCode() => base.GetHashCode();
                public Func<object?, bool> BaseEquals() => base.Equals;
            }

            public struct Plain
            {
                public int X;
                public long Y;
            }

            public static class EntryPoint
            {
                [MethodImpl(MethodImplOptions.NoInlining)]
                private static T Keep<T>(T value) => value;

                public static int Run(int input)
                {
                    var first = new Overriding { Value = input, Text = "a" + input };
                    var same = new Overriding { Value = input, Text = "a" + input };
                    var other = new Overriding { Value = input + 1, Text = "b" };
                    var plain = new Plain { X = input, Y = 2 };
                    var equals = Keep<Func<object?, bool>>(plain.Equals);
                    var hash = Keep<Func<int>>(plain.GetHashCode);
                    return (first.Equals(same) ? 1 : 0) + (first.Equals(other) ? 10 : 0) +
                        (first.GetHashCode() == same.GetHashCode() ? 100 : 0) + (first.BaseEquals()(same) ? 1000 : 0) +
                        (equals(new Plain { X = input, Y = 2 }) ? 10000 : 0) + (equals(new Plain { X = input, Y = 3 }) ? 100000 : 0) +
                        (hash() == new Plain { X = input, Y = 2 }.GetHashCode() ? 1000000 : 0);
                }
            }
            """);

        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], target,
            ProjectDirectory: assets.Directory, Optimization: optimization));
        CompilerTestSupport.ValidateWithNode(compilation.ApplicationModule, assets.Directory);

        Assert.Equal(1011101, CompilerTestSupport.ExecuteWithStandardWasiNode(
            compilation.ApplicationModule, assets.Directory, 7, target, compilation.StaticDataEnd));
    }
}
