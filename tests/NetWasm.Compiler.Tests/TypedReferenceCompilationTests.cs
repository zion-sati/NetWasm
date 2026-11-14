using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// __makeref, __refvalue and __reftype (mkrefany, refanyval, refanytype) run as on desktop: a reference writes through
// to its local, array element or struct field, ToObject boxes the referenced value as its type, a mismatched
// __refvalue throws InvalidCastException, and the reference keeps an array only it reaches alive across a collection.
public sealed class TypedReferenceCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O0)]
    public void TypedReferencesReadWriteAndBoxTheirValues(WasmTarget target, FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("TypedReferenceFixture", Source);

        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib],
            "TypedReferenceFixture.EntryPoint", "Run", [], Target: target, Optimization: optimization));

        Assert.Equal(42, ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        namespace TypedReferenceFixture;

        public struct Point
        {
            public int X;
            public int Y;
        }

        public static class EntryPoint
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static string[] MakeArray(int input) => new[] { "a", input.ToString() };

            public static int Run(int input)
            {
                int value = input;
                TypedReference reference = __makeref(value);
                if (__reftype(reference) != typeof(int)) return 1;
                __refvalue(reference, int) += 1;
                if (value != input + 1) return 2;
                if ((int)TypedReference.ToObject(reference) != input + 1) return 3;
                if (TypedReference.GetTargetType(reference) != typeof(int)) return 4;
                try
                {
                    _ = __refvalue(reference, long);
                    return 5;
                }
                catch (InvalidCastException)
                {
                }

                TypedReference element = __makeref(MakeArray(input)[1]);
                GC.Collect();
                for (var index = 0; index < 1000; index++) _ = new object[4];
                if (__refvalue(element, string) != input.ToString()) return 6;
                __refvalue(element, string) = "c";
                if ((string)TypedReference.ToObject(element) != "c") return 7;

                var point = new Point { X = 3, Y = 4 };
                TypedReference pointReference = __makeref(point);
                __refvalue(pointReference, Point).Y = input;
                if (point.Y != input || ((Point)TypedReference.ToObject(pointReference)).Y != input) return 8;
                return input + 1;
            }
        }
        """;
}
