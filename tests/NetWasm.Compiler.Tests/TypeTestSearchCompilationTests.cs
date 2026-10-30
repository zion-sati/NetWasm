using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// An interface implemented by every other type of a long run, and classes
// derived from a base among them: the matching type ids are scattered, so the
// tests search them. is, as and a failing cast answer as desktop .NET does.
public sealed class TypeTestSearchCompilationTests
{
    private const string Source = """
        using System;

        public static class EntryPoint
        {
            private interface IShape { }

            private class Base0 { }
            private class Base1 : Base0 { }
            private class Base2 : Base1 { }

            private sealed class C00 : Base0, IShape { }
            private sealed class C01 { }
            private sealed class C02 : IShape { }
            private sealed class C03 : IShape { }
            private sealed class C04 : IShape { }
            private sealed class C05 : Base2 { }
            private sealed class C06 : IShape { }
            private sealed class C07 { }
            private sealed class C08 : IShape { }
            private sealed class C09 { }
            private sealed class C10 : Base1, IShape { }
            private sealed class C11 { }
            private sealed class C12 : IShape { }
            private sealed class C13 { }
            private sealed class C14 : IShape { }
            private sealed class C15 : Base0 { }
            private sealed class C16 : IShape { }
            private sealed class C17 : IShape { }
            private sealed class C18 : IShape { }
            private sealed class C19 { }
            private sealed class C20 : Base2, IShape { }
            private sealed class C21 { }
            private sealed class C22 : IShape { }
            private sealed class C23 { }

            private static readonly object[] Values =
            [
                new C00(),
                new C01(),
                new C02(),
                new C03(),
                new C04(),
                new C05(),
                new C06(),
                new C07(),
                new C08(),
                new C09(),
                new C10(),
                new C11(),
                new C12(),
                new C13(),
                new C14(),
                new C15(),
                new C16(),
                new C17(),
                new C18(),
                new C19(),
                new C20(),
                new C21(),
                new C22(),
                new C23(),
                new Base0(), new Base1(), new Base2(), "text", new int[1], null,
            ];

            // Which values implement the interface, which derive from Base1, and how
            // many casts to the interface fail.
            public static int Run(int input)
            {
                var implementing = 0;
                var derived = 0;
                var failed = 0;
                for (var round = 0; round <= input % 3; round++)
                {
                    for (var index = 0; index < Values.Length; index++)
                    {
                        var value = Values[(index + input) % Values.Length];
                        if (value is IShape) implementing += 1 << (index % 24);
                        if (value as Base1 is not null) derived += index + 1;
                        try
                        {
                            _ = (IShape)value;
                        }
                        catch (InvalidCastException)
                        {
                            failed++;
                        }
                    }
                }
                return implementing * 7 + derived * 3 + failed;
            }
        }
        """;

    // As desktop .NET computes them.
    public static TheoryData<WasmTarget, FinalWasmOptimization, int, int> Cases
    {
        get
        {
            var data = new TheoryData<WasmTarget, FinalWasmOptimization, int, int>();
            foreach (var (input, expected) in new[]
                     {
                         (0, 40064683), (1, 40065382), (2, 30049401), (5, 3757875), (13, 170666650), (29, 240387279),
                     })
            {
                data.Add(WasmTarget.Wasm32, FinalWasmOptimization.O3, input, expected);
            }
            data.Add(WasmTarget.Wasm64, FinalWasmOptimization.O3, 13, 170666650);
            data.Add(WasmTarget.Wasm32, FinalWasmOptimization.Oz, 29, 240387279);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void TypeTestsMatchDesktop(WasmTarget target, FinalWasmOptimization optimization, int input, int expected)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource($"TypeTestSearch_{target}_{optimization}", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: optimization));

        Assert.Equal(expected, ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, input, target, result.StaticDataEnd));
    }
}
