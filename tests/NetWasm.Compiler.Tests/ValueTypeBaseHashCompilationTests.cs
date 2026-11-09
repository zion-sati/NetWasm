using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ValueTypeBaseHashCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void DirectBaseHashMatchesBoxedAndConstrainedCalls(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("BaseHashFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "BaseHashFixture.EntryPoint",
            "Run", [], Target: target));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        namespace BaseHashFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var first = new Value { Number = input };
                var equal = new Value { Number = input };
                var other = new Value { Number = input + 1 };
                if (first.GetHashCode() != equal.GetHashCode()) return 1;
                object boxed = first;
                if (boxed.GetHashCode() != first.GetHashCode()) return 2;
                if (Constrained(first) != first.GetHashCode()) return 3;
                if (other.GetHashCode() == first.GetHashCode()) return 4;
                var wide = new Generic<long> { Item = 0x100000000L + input };
                var wideCopy = wide;
                if (wide.GetHashCode() != wideCopy.GetHashCode() || ((object)wide).GetHashCode() != wide.GetHashCode()) return 5;
                var reference = new Generic<string> { Item = "sample" };
                if (reference.GetHashCode() != Constrained(reference) || ((object)reference).GetHashCode() != reference.GetHashCode()) return 6;
                return input + 1;
            }
            private static int Constrained<T>(T value) where T : struct => value.GetHashCode();
            private struct Generic<T>
            {
                public T Item;
                public override int GetHashCode() => base.GetHashCode();
            }
            private struct Value
            {
                public int Number;
                public override int GetHashCode() => base.GetHashCode();
            }
        }
        """;
}
